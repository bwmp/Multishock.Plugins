using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MultiShock.PluginSdk;

namespace JoystickIntegration.Services;

/// <summary>
/// Runs the joystick.tv OAuth2 authorization-code flow to "install" the bot on the
/// streamer's own channel. Opens the authorize page in the browser, catches the redirect
/// on a loopback HttpListener, exchanges the code for tokens, and persists them.
/// The websocket itself authenticates with the Base64(clientId:clientSecret) basic key,
/// but events only flow once the bot has been installed via this flow.
/// </summary>
public class JoystickAuthService : IDisposable
{
    private const string AuthFileName = "joystick-auth.json";

    private readonly IPluginHost _pluginHost;
    private readonly JoystickSettingsService _settings;
    private readonly string _authPath;
    private readonly HttpClient _http = new();

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<bool>? _authTcs;
    private bool _disposed;

    private class TokenState
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }

        /// <summary>Absolute expiry as a Unix-seconds timestamp.</summary>
        public long ExpiresAtUnix { get; set; }
        public string? InstalledClientId { get; set; }
    }

    private TokenState _tokens = new();
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public bool IsInstalled =>
        !string.IsNullOrEmpty(_tokens.AccessToken) &&
        _tokens.InstalledClientId == _settings.ClientId;

    public string? AccessToken => _tokens.AccessToken;

    public event Action<string>? StatusChanged;

    public JoystickAuthService(IPluginHost pluginHost, JoystickSettingsService settings)
    {
        _pluginHost = pluginHost;
        _settings = settings;
        var dataPath = _pluginHost.GetPluginDataPath(JoystickIntegrationPlugin.PluginId);
        _authPath = Path.Combine(dataPath, AuthFileName);
        Load();
    }

    /// <summary>
    /// Launches the browser install flow. Returns true once the streamer authorizes and
    /// the token exchange succeeds.
    /// </summary>
    public async Task<bool> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.HasCredentials)
        {
            StatusChanged?.Invoke("Enter your bot Client ID and Client Secret first.");
            return false;
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{JoystickConstants.OAuthCallbackPort}/");
        _listener.Prefixes.Add($"http://localhost:{JoystickConstants.OAuthCallbackPort}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            StatusChanged?.Invoke($"Failed to start local auth server on port {JoystickConstants.OAuthCallbackPort}: {ex.Message}");
            return false;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _authTcs = new TaskCompletionSource<bool>();
        var state = Guid.NewGuid().ToString("N");

        var authUrl = $"{JoystickConstants.AuthorizeUrl}" +
            $"?response_type=code" +
            $"&client_id={Uri.EscapeDataString(_settings.ClientId)}" +
            $"&scope=bot" +
            $"&state={state}";

        StatusChanged?.Invoke("Opening browser to authorize the bot on your channel...");

        try
        {
            Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Failed to open browser: {ex.Message}");
            Cleanup();
            return false;
        }

        _ = ListenForCallbackAsync(state, _cts.Token);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);
        try
        {
            var completed = await Task.WhenAny(_authTcs.Task, Task.Delay(-1, linkedCts.Token));
            if (completed == _authTcs.Task)
            {
                return await _authTcs.Task;
            }

            StatusChanged?.Invoke("Authorization timed out.");
            return false;
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("Authorization cancelled.");
            return false;
        }
        finally
        {
            Cleanup();
        }
    }

    private async Task ListenForCallbackAsync(string expectedState, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _listener?.IsListening == true)
            {
                var contextTask = _listener.GetContextAsync();
                var completed = await Task.WhenAny(contextTask, Task.Delay(-1, ct));
                if (completed != contextTask)
                {
                    break;
                }

                var context = await contextTask;
                var request = context.Request;
                var response = context.Response;

                try
                {
                    var code = request.QueryString["code"];
                    var returnedState = request.QueryString["state"];
                    var error = request.QueryString["error"];

                    if (!string.IsNullOrEmpty(error))
                    {
                        StatusChanged?.Invoke($"Authorization failed: {error}");
                        await WriteHtmlAsync(response, ResultHtml(false, error), ct);
                        _authTcs?.TrySetResult(false);
                        break;
                    }

                    if (returnedState != expectedState)
                    {
                        StatusChanged?.Invoke("Authorization failed: state mismatch (possible tampering).");
                        await WriteHtmlAsync(response, ResultHtml(false, "State mismatch"), ct);
                        _authTcs?.TrySetResult(false);
                        break;
                    }

                    if (!string.IsNullOrEmpty(code))
                    {
                        var ok = await ExchangeCodeAsync(code, ct);
                        await WriteHtmlAsync(response, ResultHtml(ok,
                            ok ? "You can close this window and return to MultiShock." : "Token exchange failed."), ct);
                        _authTcs?.TrySetResult(ok);
                        break;
                    }
                }
                finally
                {
                    response.Close();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal cancellation
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Auth callback error: {ex.Message}");
            _authTcs?.TrySetResult(false);
        }
    }

    private async Task<bool> ExchangeCodeAsync(string code, CancellationToken ct)
    {
        try
        {
            var url = $"{JoystickConstants.TokenUrl}" +
                $"?redirect_uri=unused" +
                $"&code={Uri.EscapeDataString(code)}" +
                $"&grant_type=authorization_code";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Basic " + _settings.BasicKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                StatusChanged?.Invoke($"Token exchange failed ({(int)resp.StatusCode}): {body}");
                return false;
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(body);
            if (token?.AccessToken is null)
            {
                StatusChanged?.Invoke("Token exchange returned no access_token.");
                return false;
            }

            _tokens.AccessToken = token.AccessToken;
            _tokens.RefreshToken = token.RefreshToken;
            _tokens.ExpiresAtUnix = NormalizeExpiry(token.ExpiresIn);
            _tokens.InstalledClientId = _settings.ClientId;
            Save();

            StatusChanged?.Invoke("Bot installed successfully!");
            return true;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Token exchange error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Refreshes the access token if it is missing or within 2 minutes of expiry.
    /// Best-effort: failures are logged via StatusChanged but do not throw (the websocket
    /// uses the basic key, so a stale REST token does not break event streaming).
    /// </summary>
    public async Task EnsureFreshTokenAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_tokens.RefreshToken) || _tokens.InstalledClientId != _settings.ClientId)
        {
            return;
        }

        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_tokens.ExpiresAtUnix > nowUnix + 120)
        {
            return; // still valid
        }

        await RefreshAsync(ct);
    }

    /// <summary>Returns a valid access token, refreshing first if needed. Null if not installed.</summary>
    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        await EnsureFreshTokenAsync(ct);
        return _tokens.AccessToken;
    }

    private async Task<bool> RefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Re-check under the lock in case another caller just refreshed.
            var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (_tokens.ExpiresAtUnix > nowUnix + 120 || string.IsNullOrEmpty(_tokens.RefreshToken))
            {
                return true;
            }

            var url = $"{JoystickConstants.TokenUrl}" +
                $"?grant_type=refresh_token" +
                $"&refresh_token={Uri.EscapeDataString(_tokens.RefreshToken!)}";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Basic " + _settings.BasicKey);
            req.Headers.TryAddWithoutValidation("Accept", "application/json");
            req.Content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");

            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                StatusChanged?.Invoke($"Token refresh failed ({(int)resp.StatusCode}). You may need to reinstall the bot.");
                return false;
            }

            var token = JsonSerializer.Deserialize<TokenResponse>(body);
            if (token?.AccessToken is null)
            {
                return false;
            }

            _tokens.AccessToken = token.AccessToken;
            if (!string.IsNullOrEmpty(token.RefreshToken))
            {
                _tokens.RefreshToken = token.RefreshToken;
            }
            _tokens.ExpiresAtUnix = NormalizeExpiry(token.ExpiresIn);
            Save();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Token refresh error: {ex.Message}");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Joystick.tv returns <c>expires_in</c> as an absolute Unix timestamp in its docs, but treat a
    /// small value as a relative "seconds from now" duration for safety.
    /// </summary>
    private static long NormalizeExpiry(long expiresIn)
    {
        const long absoluteThreshold = 1_000_000_000; // ~2001 in Unix seconds
        return expiresIn > absoluteThreshold
            ? expiresIn
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn;
    }

    public void ClearInstall()
    {
        _tokens = new TokenState();
        Save();
        StatusChanged?.Invoke("Removed stored bot installation.");
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_authPath))
            {
                var json = File.ReadAllText(_authPath);
                _tokens = JsonSerializer.Deserialize<TokenState>(json) ?? new TokenState();
            }
        }
        catch
        {
            _tokens = new TokenState();
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_tokens, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_authPath, json);
        }
        catch
        {
            // Swallow persistence errors
        }
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html, CancellationToken ct)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, ct);
    }

    private static string ResultHtml(bool success, string message)
    {
        var color = success ? "#22c55e" : "#ef4444";
        var title = success ? "Bot Installed!" : "Authorization Failed";
        return $$"""
            <!DOCTYPE html><html><head><title>MultiShock - Joystick.tv</title>
            <style>
                body { font-family: -apple-system, 'Segoe UI', Roboto, sans-serif;
                    background: linear-gradient(135deg, #1a1a2e 0%, #2a1a4e 100%);
                    color: white; display: flex; justify-content: center; align-items: center;
                    height: 100vh; margin: 0; }
                .container { text-align: center; padding: 2rem; }
                h1 { color: {{color}}; margin-bottom: 0.5rem; }
                p { color: #a0a0a0; max-width: 400px; }
            </style></head>
            <body><div class="container"><h1>{{title}}</h1><p>{{message}}</p></div></body></html>
            """;
    }

    public static bool IsPortAvailable()
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, JoystickConstants.OAuthCallbackPort);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void CancelInstall()
    {
        _cts?.Cancel();
        _authTcs?.TrySetResult(false);
    }

    private void Cleanup()
    {
        try
        {
            _listener?.Stop();
            _listener?.Close();
        }
        catch { }

        _listener = null;
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelInstall();
        Cleanup();
        _http.Dispose();
    }

    private class TokenResponse
    {
        [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
        [JsonPropertyName("expires_in")] public long ExpiresIn { get; set; }
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    }
}
