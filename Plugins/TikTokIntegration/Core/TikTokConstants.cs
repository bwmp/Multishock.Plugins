namespace TikTokIntegration;

public static class TikTokConstants
{
    public const string WebUrl = "https://www.tiktok.com/";
    public const string WebcastUrl = "https://webcast.tiktok.com/webcast/";
    public const string Origin = "https://www.tiktok.com";

    /// <summary>Default signing provider (Euler Stream). TikTok requires signed connection
    /// parameters (X-Bogus/X-Gnarly/msToken) that cannot be computed client-side, so the sign
    /// server proxies the initial webcast fetch and returns the signed WebcastResponse directly.
    /// As of the v2 API the endpoint is GET {host}/webcast/rooms/{roomId}/connect.</summary>
    public const string DefaultSignProviderHost = "https://api.eulerstream.com";

    /// <summary>Identifier sent as the sign server "client" query param. Not validated against an
    /// allowlist, but useful for the provider's own analytics.</summary>
    public const string SignClientName = "multishock";

    public const string DefaultUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public const string WebcastVersionCode = "180800";

    /// <summary>Client parameters TikTok's web player sends with every webcast request.</summary>
    public static Dictionary<string, string> CreateDefaultClientParams() => new()
    {
        ["aid"] = "1988",
        ["app_language"] = "en-US",
        ["app_name"] = "tiktok_web",
        ["browser_language"] = "en",
        ["browser_name"] = "Mozilla",
        ["browser_online"] = "true",
        ["browser_platform"] = "Win32",
        ["browser_version"] = "5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/106.0.0.0 Safari/537.36",
        ["cookie_enabled"] = "true",
        ["cursor"] = "",
        ["internal_ext"] = "",
        ["device_platform"] = "web",
        ["focus_state"] = "true",
        ["from_page"] = "user",
        ["history_len"] = "0",
        ["is_fullscreen"] = "false",
        ["is_page_visible"] = "true",
        ["did_rule"] = "3",
        ["fetch_rule"] = "1",
        ["last_rtt"] = "0",
        ["live_id"] = "12",
        ["resp_content_type"] = "protobuf",
        ["screen_height"] = "1152",
        ["screen_width"] = "2048",
        ["tz_name"] = "Europe/Berlin",
        ["referer"] = "https://www.tiktok.com/",
        ["root_referer"] = "https://www.tiktok.com/",
        ["host"] = "https://webcast.tiktok.com",
        ["webcast_sdk_version"] = "1.3.0",
        ["update_version_code"] = "1.3.0",
    };
}
