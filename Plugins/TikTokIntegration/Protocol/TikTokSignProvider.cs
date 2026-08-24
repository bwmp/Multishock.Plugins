using System.Net.Http.Headers;

namespace TikTokIntegration.Protocol;

public sealed class TikTokLiveException : Exception
{
    public TikTokLiveException(string message) : base(message) { }
    public TikTokLiveException(string message, Exception inner) : base(message, inner) { }
}

public sealed class UserOfflineException : Exception
{
    public UserOfflineException(string message) : base(message) { }
}

public sealed class SignServerException : Exception
{
    public SignServerException(string message) : base(message) { }
    public SignServerException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>The signed initial connection payload proxied from the sign server.</summary>
public sealed class SignedConnect
{
    /// <summary>Raw <c>WebcastResponse</c> protobuf bytes (uncompressed) returned by the sign server.</summary>
    public required byte[] Payload { get; init; }

    /// <summary>Value of the <c>X-Set-TT-Cookie</c> header — TikTok cookies (ttwid, etc.) that must
    /// be attached to the subsequent websocket handshake.</summary>
    public string? TikTokCookies { get; init; }
}

/// <summary>
/// Talks to the Euler Stream sign server's v2 API. Rather than signing a URL for the client to
/// fetch (the old <c>webcast/sign_url</c> route, now removed), the server proxies the initial
/// <c>im/fetch</c> to TikTok using its own signatures and returns the resulting WebcastResponse
/// protobuf plus the TikTok cookies to reuse. Works anonymously; an API key raises the rate limit.
/// </summary>
public sealed class TikTokSignProvider
{
    private readonly HttpClient _httpClient;

    public string? ApiKey { get; set; }

    /// <summary>Optional override of the sign server host (e.g. a self-hosted instance).</summary>
    public string? CustomHost { get; set; }

    public TikTokSignProvider()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", $"multishock-tiktok/{BuildStamp.Version}");
    }

    public async Task<SignedConnect> FetchConnectAsync(
        string roomId,
        string userAgent,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var host = string.IsNullOrWhiteSpace(CustomHost)
            ? TikTokConstants.DefaultSignProviderHost
            : CustomHost.TrimEnd('/');

        var query = new List<string>
        {
            $"client={Uri.EscapeDataString(TikTokConstants.SignClientName)}",
            "client_enter=true",
            "platform=web",
            $"user_agent={Uri.EscapeDataString(userAgent)}",
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            query.Add($"session_id={Uri.EscapeDataString(sessionId)}");
        }

        var requestUrl = $"{host}/webcast/rooms/{Uri.EscapeDataString(roomId)}/connect?{string.Join("&", query)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        if (!string.IsNullOrWhiteSpace(ApiKey))
        {
            request.Headers.TryAddWithoutValidation("X-Api-Key", ApiKey);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new SignServerException(
                $"Could not reach the sign server ({host}). Check your internet connection. ({ex.Message})", ex);
        }

        using (response)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                var retry = response.Headers.RetryAfter?.Delta?.TotalSeconds
                            ?? TryGetHeaderSeconds(response, "ratelimit-reset");
                var suffix = retry.HasValue ? $" Try again in ~{(int)retry.Value}s." : "";
                throw new SignServerException(
                    string.IsNullOrWhiteSpace(ApiKey)
                        ? $"Sign server rate limit reached.{suffix} Add a free Euler Stream API key to raise it."
                        : $"Sign server rate limit reached for your API key.{suffix}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = SafeText(bytes);
                var message = ExtractJsonMessage(body) ?? body;

                // 402: room resolution / mobile features gated behind a paid plan
                if ((int)response.StatusCode == 402)
                {
                    throw new SignServerException($"Sign server: {message}");
                }

                throw new SignServerException(
                    $"Sign server returned {(int)response.StatusCode}: {Truncate(message)}");
            }

            if (bytes.Length == 0)
            {
                throw new SignServerException(
                    "Sign server returned an empty response. The streamer may have just gone offline, or TikTok is blocking the sign server.");
            }

            return new SignedConnect
            {
                Payload = bytes,
                TikTokCookies = response.Headers.TryGetValues("X-Set-TT-Cookie", out var cookies)
                    ? string.Join("; ", cookies)
                    : null,
            };
        }
    }

    private static double? TryGetHeaderSeconds(HttpResponseMessage response, string header)
    {
        if (response.Headers.TryGetValues(header, out var values) &&
            double.TryParse(values.FirstOrDefault(), out var seconds))
        {
            return seconds;
        }
        return null;
    }

    private static string SafeText(byte[] bytes)
    {
        try { return System.Text.Encoding.UTF8.GetString(bytes); }
        catch { return ""; }
    }

    private static string? ExtractJsonMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body) || !body.TrimStart().StartsWith('{'))
        {
            return null;
        }
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var msg) &&
                msg.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return msg.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string Truncate(string value) =>
        value.Length <= 200 ? value : value[..200] + "...";
}
