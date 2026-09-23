using System.Net.Http.Headers;
using System.Text.Json;
using MultiShock.PluginSdk;

namespace FanslyIntegration.Protocol;

/// <summary>
/// Thin REST client for the (undocumented) Fansly API. Its only job is to resolve the pieces
/// the websocket needs: the streamer's account id and their live chat-room id. Everything is
/// best-effort and defensive — if Fansly changes a field name we surface a clear error rather
/// than throwing.
/// </summary>
public sealed class FanslyApiClient
{
    private readonly FanslySettingsService _settings;
    private readonly HttpClient _http = new();

    public FanslyApiClient(FanslySettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Resolves the account id for the configured username. Null on failure.</summary>
    public async Task<string?> GetAccountIdAsync(CancellationToken ct = default)
    {
        var username = Uri.EscapeDataString(_settings.Username);
        var json = await GetJsonAsync($"{FanslyConstants.ApiBase}/account?usernames={username}", ct);
        if (json is null) return null;

        // Response shape: { "success": true, "response": [ { "id": "...", "username": "..." } ] }
        if (json.Value.TryGetProperty("response", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var acc in arr.EnumerateArray())
            {
                if (acc.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String)
                {
                    return idEl.GetString();
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves the active chat-room id for the given account's live stream. Null if the
    /// streamer is offline or the field could not be located.
    /// </summary>
    public async Task<string?> GetChatRoomIdAsync(string accountId, CancellationToken ct = default)
    {
        var json = await GetJsonAsync($"{FanslyConstants.ApiBase}/streaming/channel/{accountId}", ct);
        if (json is null) return null;

        if (!json.Value.TryGetProperty("response", out var resp) || resp.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // The chat-room id has lived under a few names across client versions; try the likely ones.
        foreach (var key in new[] { "chatRoomId", "chatroomId", "chatRoom", "id" })
        {
            if (resp.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }
        }

        // Sometimes nested under a "stream" object.
        if (resp.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.Object &&
            stream.TryGetProperty("chatRoomId", out var nested) && nested.ValueKind == JsonValueKind.String)
        {
            return nested.GetString();
        }

        return null;
    }

    private async Task<JsonElement?> GetJsonAsync(string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", _settings.AuthToken);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }
}
