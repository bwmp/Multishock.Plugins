using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using MultiShock.PluginSdk;
using MultiShock.Plugins.Shared;

namespace TwitchIntegration.Services;

public class TwitchAuthService : IDisposable
{
    private const string AuthSettingsFileName = "twitch-auth.json";
    private const string AccessTokenSecret = "access-token";
    private const string RefreshTokenSecret = "refresh-token";
    private const string DeviceEndpoint = "https://id.twitch.tv/oauth2/device";
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    // Checking at half the lead time guarantees a refresh happens before the 4 hour token expires.
    private static readonly TimeSpan RefreshLeadTime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RefreshCheckInterval = TimeSpan.FromMinutes(15);

    private readonly IPluginHost _pluginHost;
    private readonly string _authSettingsPath;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly Timer _refreshTimer;
    private readonly PluginSecrets _secrets;
    private CancellationTokenSource? _loginCts;
    private string? _secretsSaveError;
    private bool _disposed;

    private class AuthState
    {
        // OAuthToken and RefreshToken are only read to move values saved by older versions into secure storage.
        public string? OAuthToken { get; set; }
        public string? RefreshToken { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public bool AutoConnect { get; set; }
        public bool UseLocalCli { get; set; }
        public string LocalCliUrl { get; set; } = TwitchConstants.DefaultLocalCliUrl;
    }

    private AuthState _state = new();

    public string? StoredToken => _secrets.Get(AccessTokenSecret);

    public bool CanRefresh => !string.IsNullOrEmpty(_secrets.Get(RefreshTokenSecret));

    /// <summary>Why the Twitch login isn't stored securely or couldn't be read or saved, or null.</summary>
    public string? SecretsWarning => _secretsSaveError ?? _secrets.Warning;

    public bool LoginExpired { get; private set; }

    public string? PendingUserCode { get; private set; }

    public string? PendingVerificationUri { get; private set; }

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

    public string LocalCliUrl
    {
        get => _state.LocalCliUrl;
        set
        {
            _state.LocalCliUrl = value;
            SaveAuthState();
        }
    }

    public string EventSubWebSocketUrl => UseLocalCli ? LocalCliUrl : TwitchConstants.EventSubWebSocketUrl;

    // The CLI serves its mock subscription API on the same host and port as its WebSocket server.
    public string HelixApiBaseUrl
    {
        get
        {
            if (!UseLocalCli)
            {
                return TwitchConstants.HelixApiBaseUrl;
            }

            var wsUri = new Uri(LocalCliUrl);
            var scheme = wsUri.Scheme == "wss" ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
            return $"{scheme}://{wsUri.Authority}";
        }
    }

    public static bool IsValidLocalCliUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "ws" || uri.Scheme == "wss");

    public event Action<string>? StatusChanged;

    public TwitchAuthService(IPluginHost pluginHost, IServiceProvider services) : this(pluginHost, services, new HttpClientHandler())
    {
    }

    internal TwitchAuthService(IPluginHost pluginHost, IServiceProvider services, HttpMessageHandler httpHandler)
    {
        _pluginHost = pluginHost;
        _httpClient = new HttpClient(httpHandler);
        _secrets = new PluginSecrets(services, pluginHost, TwitchIntegrationPlugin.PluginId);
        var dataPath = _pluginHost.GetPluginDataPath(TwitchIntegrationPlugin.PluginId);
        _authSettingsPath = Path.Combine(dataPath, AuthSettingsFileName);
        LoadAuthState();
        ImportLegacyTokens();
        _refreshTimer = new Timer(_ => _ = RefreshOnScheduleAsync(), null, RefreshCheckInterval, RefreshCheckInterval);
    }

    public async Task<string?> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        CancelAuthentication();
        var loginCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loginCts = loginCts;
        var scopes = string.Join(' ', TwitchConstants.RequiredScopes);

        try
        {
            using var deviceResponse = await _httpClient.PostAsync(DeviceEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = TwitchConstants.ClientId,
                ["scopes"] = scopes,
            }), loginCts.Token);

            if (!deviceResponse.IsSuccessStatusCode)
            {
                StatusChanged?.Invoke($"Twitch login failed: {await ReadErrorAsync(deviceResponse, loginCts.Token)}");
                return null;
            }

            var device = await deviceResponse.Content.ReadFromJsonAsync<DeviceCodeResponse>(loginCts.Token);
            if (device == null || string.IsNullOrEmpty(device.DeviceCode))
            {
                StatusChanged?.Invoke("Twitch login failed: empty device code response");
                return null;
            }

            PendingUserCode = device.UserCode;
            PendingVerificationUri = device.VerificationUri;
            StatusChanged?.Invoke($"Approve MultiShock on Twitch with code {device.UserCode}");
            OpenBrowser(device.VerificationUri);

            var interval = TimeSpan.FromSeconds(Math.Max(device.Interval, 1));
            var deadline = DateTime.UtcNow.AddSeconds(device.ExpiresIn);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(interval, loginCts.Token);

                using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = TwitchConstants.ClientId,
                    ["scopes"] = scopes,
                    ["device_code"] = device.DeviceCode,
                    ["grant_type"] = DeviceCodeGrantType,
                }), loginCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var token = await response.Content.ReadFromJsonAsync<TokenResponse>(loginCts.Token);
                    if (token == null || string.IsNullOrEmpty(token.AccessToken))
                    {
                        StatusChanged?.Invoke("Twitch login failed: empty token response");
                        return null;
                    }

                    if (StoreTokens(token) is { } saveError)
                    {
                        StatusChanged?.Invoke(saveError);
                        return null;
                    }

                    StatusChanged?.Invoke("Logged in to Twitch.");
                    return token.AccessToken;
                }

                var error = await ReadErrorAsync(response, loginCts.Token);
                if (error == "authorization_pending") continue;
                if (error == "slow_down")
                {
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                }

                StatusChanged?.Invoke($"Twitch login failed: {error}");
                return null;
            }

            StatusChanged?.Invoke("Twitch login code expired, start the login again");
            return null;
        }
        catch (OperationCanceledException) when (loginCts.IsCancellationRequested)
        {
            StatusChanged?.Invoke("Authentication cancelled");
            return null;
        }
        catch (HttpRequestException ex)
        {
            StatusChanged?.Invoke($"Twitch login failed: {ex.Message}");
            return null;
        }
        finally
        {
            PendingUserCode = null;
            PendingVerificationUri = null;
            Interlocked.CompareExchange(ref _loginCts, null, loginCts);
            loginCts.Dispose();
        }
    }

    public void CancelAuthentication()
    {
        try
        {
            _loginCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The login finished between reading the field and cancelling it.
        }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (CanRefresh && _state.ExpiresAtUtc is { } expiresAt && expiresAt - DateTime.UtcNow < RefreshLeadTime)
        {
            await RefreshAccessTokenAsync(cancellationToken);
        }

        return StoredToken;
    }

    public async Task<bool> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var refreshToken = _secrets.Get(RefreshTokenSecret);
        if (string.IsNullOrEmpty(refreshToken)) return false;

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            // Refresh tokens are single use, so callers queued behind a refresh reuse its result.
            if (_secrets.Get(RefreshTokenSecret) != refreshToken)
            {
                return !string.IsNullOrEmpty(StoredToken);
            }

            using var response = await _httpClient.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = TwitchConstants.ClientId,
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            }), cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
                if (token != null && !string.IsNullOrEmpty(token.AccessToken))
                {
                    if (StoreTokens(token) is not null)
                    {
                        return false;
                    }

                    TwitchIntegrationPlugin.Logger?.LogInformation("[Auth] Twitch access token refreshed, expires {ExpiresAt:u}", _state.ExpiresAtUtc);
                    return true;
                }
            }

            var error = await ReadErrorAsync(response, cancellationToken);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            {
                MarkLoginExpired($"refresh rejected ({error})");
            }
            else
            {
                TwitchIntegrationPlugin.Logger?.LogWarning("[Auth] Twitch token refresh failed: {StatusCode} {Error}", (int)response.StatusCode, error);
            }

            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            TwitchIntegrationPlugin.Logger?.LogWarning("[Auth] Twitch token refresh failed: {Error}", ex.Message);
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public void MarkLoginExpired(string reason)
    {
        if (LoginExpired) return;

        SaveTokens(null, null, null);
        LoginExpired = true;

        TwitchIntegrationPlugin.Logger?.LogWarning("[Auth] Twitch login expired: {Reason}", reason);
        StatusChanged?.Invoke("Twitch login expired. Log in again to reconnect.");
        _pluginHost.ShowWarningToast("Twitch login expired",
            "Log in again on the Twitch Integration page to keep receiving Twitch events.", isPersistent: true);
    }

    public void SaveAuthToken(string token)
    {
        LoginExpired = false;
        SaveTokens(token, null, null);
    }

    public void ClearAuth()
    {
        _state.AutoConnect = false;
        LoginExpired = false;
        StatusChanged?.Invoke(SaveTokens(null, null, null) ?? "Logged out of Twitch");
    }

    private string? StoreTokens(TokenResponse token)
    {
        LoginExpired = false;
        return SaveTokens(token.AccessToken, token.RefreshToken, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
    }

    // Also runs from background refreshes, so a failure is logged, kept for SecretsWarning and returned instead of thrown.
    private string? SaveTokens(string? accessToken, string? refreshToken, DateTime? expiresAtUtc)
    {
        try
        {
            _secrets.Set(AccessTokenSecret, accessToken);
            _secrets.Set(RefreshTokenSecret, refreshToken);
            _state.ExpiresAtUtc = expiresAtUtc;
            _secretsSaveError = null;
            return null;
        }
        catch (Exception ex)
        {
            _secretsSaveError = $"Couldn't update the saved Twitch login: {ex.Message}";
            TwitchIntegrationPlugin.Logger?.LogError(ex, "[Auth] Couldn't update the saved Twitch login");
            return _secretsSaveError;
        }
        finally
        {
            SaveAuthState();
        }
    }

    private void ImportLegacyTokens()
    {
        var imported = false;
        if (_secrets.Import(AccessTokenSecret, _state.OAuthToken))
        {
            _state.OAuthToken = null;
            imported = true;
        }

        if (_secrets.Import(RefreshTokenSecret, _state.RefreshToken))
        {
            _state.RefreshToken = null;
            imported = true;
        }

        if (imported)
        {
            SaveAuthState();
        }
    }

    private async Task RefreshOnScheduleAsync()
    {
        try
        {
            await GetAccessTokenAsync();
        }
        catch (Exception ex)
        {
            TwitchIntegrationPlugin.Logger?.LogError(ex, "[Auth] Scheduled Twitch token refresh failed");
        }
    }

    private void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        {
            StatusChanged?.Invoke($"Couldn't open the browser ({ex.Message}). Open {url} manually.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not a JSON error body; report it as-is.
        }

        return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : body;
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        CancelAuthentication();
        _refreshTimer.Dispose();
        _httpClient.Dispose();
    }

    private sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = "";

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = "";

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
