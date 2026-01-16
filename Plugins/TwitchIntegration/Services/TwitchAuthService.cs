using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;
using MultiShock.PluginSdk;

namespace TwitchIntegration.Services;

public class TwitchAuthService : IDisposable
{
    private const string PluginId = "com.multishock.twitchintegration";
    private const string AuthSettingsFileName = "twitch-auth.json";

    private readonly IPluginHost _pluginHost;
    private readonly string _authSettingsPath;

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<string?>? _authTcs;
    private int _port;
    private bool _disposed;

    private class AuthState
    {
        public string? OAuthToken { get; set; }
        public bool AutoConnect { get; set; }
        public bool UseLocalCli { get; set; }
    }

    private AuthState _state = new();

    public string? StoredToken => _state.OAuthToken;

    public bool AutoConnect
    {
        get => _state.AutoConnect;
        set
        {
            _state.AutoConnect = value;
            SaveAuthState();
        }
    }

    public bool UseLocalCli
    {
        get => _state.UseLocalCli;
        set
        {
            _state.UseLocalCli = value;
            SaveAuthState();
        }
    }

    public event Action<string>? StatusChanged;

    public TwitchAuthService(IPluginHost pluginHost)
    {
        _pluginHost = pluginHost;
        var dataPath = _pluginHost.GetPluginDataPath(PluginId);
        _authSettingsPath = Path.Combine(dataPath, AuthSettingsFileName);
        LoadAuthState();
    }

    public async Task<string?> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        _port = TwitchConstants.OAuthCallbackPort;
        var redirectUri = TwitchConstants.OAuthRedirectUri;

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{_port}/");

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            StatusChanged?.Invoke($"Failed to start auth server: {ex.Message}");
            return null;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _authTcs = new TaskCompletionSource<string?>();

        // Build the Twitch authorization URL
        var scopes = string.Join("+", TwitchConstants.RequiredScopes);
        var state = Guid.NewGuid().ToString("N"); // CSRF protection

        var authUrl = $"https://id.twitch.tv/oauth2/authorize" +
            $"?client_id={TwitchConstants.ClientId}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=token" +
            $"&scope={scopes}" +
            $"&state={state}" +
            $"&force_verify=true";

        StatusChanged?.Invoke("Opening browser for Twitch login...");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Failed to open browser: {ex.Message}");
            Cleanup();
            return null;
        }

        _ = ListenForCallbackAsync(state, _cts.Token);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, timeoutCts.Token);

        try
        {
            // Wait for the token with timeout
            var tokenTask = _authTcs.Task;
            var completedTask = await Task.WhenAny(tokenTask, Task.Delay(-1, linkedCts.Token));

            if (completedTask == tokenTask)
            {
                return await tokenTask;
            }

            StatusChanged?.Invoke("Authentication timed out");
            return null;
        }
        catch (OperationCanceledException)
        {
            StatusChanged?.Invoke("Authentication cancelled");
            return null;
        }
        finally
        {
            Cleanup();
        }
    }

    public void SaveAuthToken(string token)
    {
        _state.OAuthToken = token;
        SaveAuthState();
    }

    public void ClearAuth()
    {
        _state.OAuthToken = null;
        _state.AutoConnect = false;
        SaveAuthState();
        StatusChanged?.Invoke("Logged out of Twitch");
    }

    private async Task ListenForCallbackAsync(string expectedState, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
            {
                var contextTask = _listener.GetContextAsync();
                var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, cancellationToken));

                if (completedTask != contextTask)
                {
                    break;
                }

                var context = await contextTask;
                var request = context.Request;
                var response = context.Response;

                try
                {
                    if (request.Url?.AbsolutePath == "/callback")
                    {
                        var html = GetCallbackHtml();
                        var buffer = Encoding.UTF8.GetBytes(html);
                        response.ContentType = "text/html";
                        response.ContentLength64 = buffer.Length;
                        await response.OutputStream.WriteAsync(buffer, cancellationToken);
                    }
                    else if (request.Url?.AbsolutePath == "/token")
                    {
                        var query = request.Url.Query;
                        var queryParams = HttpUtility.ParseQueryString(query);
                        var token = queryParams["access_token"];
                        var state = queryParams["state"];
                        var error = queryParams["error"];

                        if (!string.IsNullOrEmpty(error))
                        {
                            var errorDesc = queryParams["error_description"] ?? error;
                            StatusChanged?.Invoke($"Authentication failed: {errorDesc}");

                            var errorHtml = GetResultHtml(false, errorDesc);
                            var errorBuffer = Encoding.UTF8.GetBytes(errorHtml);
                            response.ContentType = "text/html";
                            response.ContentLength64 = errorBuffer.Length;
                            await response.OutputStream.WriteAsync(errorBuffer, cancellationToken);

                            _authTcs?.TrySetResult(null);
                            break;
                        }

                        if (state != expectedState)
                        {
                            StatusChanged?.Invoke("Authentication failed: Invalid state (possible CSRF attack)");
                            _authTcs?.TrySetResult(null);
                            break;
                        }

                        if (!string.IsNullOrEmpty(token))
                        {
                            StatusChanged?.Invoke("Authentication successful!");

                            var successHtml = GetResultHtml(true, "You can close this window and return to MultiShock.");
                            var successBuffer = Encoding.UTF8.GetBytes(successHtml);
                            response.ContentType = "text/html";
                            response.ContentLength64 = successBuffer.Length;
                            await response.OutputStream.WriteAsync(successBuffer, cancellationToken);

                            _authTcs?.TrySetResult(token);
                            break;
                        }
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
            // Normal cancellation
        }
        catch (Exception ex)
        {
            StatusChanged?.Invoke($"Auth callback error: {ex.Message}");
            _authTcs?.TrySetResult(null);
        }
    }

    private void LoadAuthState()
    {
        try
        {
            if (File.Exists(_authSettingsPath))
            {
                var json = File.ReadAllText(_authSettingsPath);
                _state = JsonSerializer.Deserialize<AuthState>(json) ?? new AuthState();
            }
        }
        catch
        {
            _state = new AuthState();
        }
    }

    private void SaveAuthState()
    {
        try
        {
            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_authSettingsPath, json);
        }
        catch
        {
            // Swallow persistence errors; not critical for runtime behavior
        }
    }

    private static string GetCallbackHtml()
    {
        return """
            <!DOCTYPE html>
            <html>
            <head>
                <title>MultiShock - Twitch Login</title>
                <style>
                    body {
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
                        color: white;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        height: 100vh;
                        margin: 0;
                    }
                    .container {
                        text-align: center;
                        padding: 2rem;
                    }
                    .spinner {
                        width: 50px;
                        height: 50px;
                        border: 4px solid rgba(255,255,255,0.3);
                        border-top-color: #9146FF;
                        border-radius: 50%;
                        animation: spin 1s linear infinite;
                        margin: 0 auto 1rem;
                    }
                    @keyframes spin {
                        to { transform: rotate(360deg); }
                    }
                    h1 { color: #9146FF; margin-bottom: 0.5rem; }
                    p { color: #a0a0a0; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="spinner"></div>
                    <h1>Authenticating...</h1>
                    <p>Please wait while we complete the login.</p>
                </div>
                <script>
                    // Extract token from URL fragment
                    const hash = window.location.hash.substring(1);
                    const params = new URLSearchParams(hash);
                    
                    const accessToken = params.get('access_token');
                    const state = params.get('state');
                    const error = params.get('error');
                    const errorDescription = params.get('error_description');
                    
                    // Send to our local server
                    let url = '/token?';
                    if (error) {
                        url += 'error=' + encodeURIComponent(error);
                        if (errorDescription) {
                            url += '&error_description=' + encodeURIComponent(errorDescription);
                        }
                    } else if (accessToken) {
                        url += 'access_token=' + encodeURIComponent(accessToken) + '&state=' + encodeURIComponent(state);
                    } else {
                        url += 'error=no_token&error_description=' + encodeURIComponent('No access token received');
                    }
                    
                    window.location.href = url;
                </script>
            </body>
            </html>
            """;
    }

    private static string GetResultHtml(bool success, string message)
    {
        var color = success ? "#00C853" : "#FF5252";
        var icon = success
            ? """<svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="#00C853" stroke-width="2"><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14"></path><polyline points="22 4 12 14.01 9 11.01"></polyline></svg>"""
            : """<svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="#FF5252" stroke-width="2"><circle cx="12" cy="12" r="10"></circle><line x1="15" y1="9" x2="9" y2="15"></line><line x1="9" y1="9" x2="15" y2="15"></line></svg>""";
        var title = success ? "Success!" : "Authentication Failed";

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>MultiShock - {{title}}</title>
                <style>
                    body {
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        background: linear-gradient(135deg, #1a1a2e 0%, #16213e 100%);
                        color: white;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        height: 100vh;
                        margin: 0;
                    }
                    .container {
                        text-align: center;
                        padding: 2rem;
                    }
                    .icon { margin-bottom: 1rem; }
                    h1 { color: {{color}}; margin-bottom: 0.5rem; }
                    p { color: #a0a0a0; max-width: 400px; }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="icon">{{icon}}</div>
                    <h1>{{title}}</h1>
                    <p>{{message}}</p>
                </div>
            </body>
            </html>
            """;
    }

    public static bool IsPortAvailable()
    {
        try
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, TwitchConstants.OAuthCallbackPort);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void CancelAuthentication()
    {
        _cts?.Cancel();
        _authTcs?.TrySetResult(null);
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

        CancelAuthentication();
        Cleanup();
    }
}
