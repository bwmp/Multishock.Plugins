using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MultiShock.PluginSdk;
using TwitchIntegration.Services;

namespace TwitchIntegration.Tests;

public sealed class TwitchAuthServiceTests
{
    [Fact]
    public async Task DeviceLoginPollsUntilApprovedAndStoresRefreshableToken()
    {
        using var temp = new TwitchEventSubTests.TemporaryDirectory();
        var polls = 0;
        var handler = new FakeTwitch(async request =>
        {
            var form = await ReadFormAsync(request);
            if (request.RequestUri!.AbsolutePath == "/oauth2/device")
            {
                Assert.Equal(TwitchConstants.ClientId, form["client_id"]);
                Assert.Equal(string.Join(' ', TwitchConstants.RequiredScopes), form["scopes"]);
                // Not a URL, so the test doesn't open a real browser.
                return Json(HttpStatusCode.OK, new { device_code = "device-1", user_code = "ABCD1234", verification_uri = "not-a-browser-target", expires_in = 60, interval = 1 });
            }

            Assert.Equal("device-1", form["device_code"]);
            Assert.Equal("urn:ietf:params:oauth:grant-type:device_code", form["grant_type"]);
            return Interlocked.Increment(ref polls) == 1
                ? Json(HttpStatusCode.BadRequest, new { status = 400, message = "authorization_pending" })
                : Json(HttpStatusCode.OK, new { access_token = "access-1", refresh_token = "refresh-1", expires_in = 14400 });
        });
        using var auth = CreateAuth(temp.Path, handler, out _);
        string? codeShown = null;
        auth.StatusChanged += _ => codeShown ??= auth.PendingUserCode;

        var token = await auth.AuthenticateAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("access-1", token);
        Assert.Equal("ABCD1234", codeShown);
        Assert.Equal(2, polls);
        Assert.Equal("access-1", auth.StoredToken);
        Assert.True(auth.CanRefresh);
        Assert.Null(auth.PendingUserCode);
    }

    [Fact]
    public async Task ExpiringTokenIsRefreshedOnceForConcurrentCallers()
    {
        using var temp = new TwitchEventSubTests.TemporaryDirectory();
        WriteAuthState(temp.Path, "access-old", "refresh-old", DateTime.UtcNow.AddMinutes(5));
        var refreshes = 0;
        var handler = new FakeTwitch(async request =>
        {
            var form = await ReadFormAsync(request);
            Assert.Equal("refresh_token", form["grant_type"]);
            Assert.Equal("refresh-old", form["refresh_token"]);
            Interlocked.Increment(ref refreshes);
            await Task.Delay(50);
            return Json(HttpStatusCode.OK, new { access_token = "access-new", refresh_token = "refresh-new", expires_in = 14400 });
        });
        using var auth = CreateAuth(temp.Path, handler, out _);

        var tokens = await Task.WhenAll(auth.GetAccessTokenAsync(), auth.GetAccessTokenAsync(), auth.GetAccessTokenAsync());

        Assert.All(tokens, token => Assert.Equal("access-new", token));
        Assert.Equal(1, refreshes);
        Assert.Equal("access-new", await auth.GetAccessTokenAsync());
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task RejectedRefreshRequiresLoginAndWarnsOnce()
    {
        using var temp = new TwitchEventSubTests.TemporaryDirectory();
        WriteAuthState(temp.Path, "access-old", "refresh-old", DateTime.UtcNow.AddMinutes(-5));
        var handler = new FakeTwitch(_ => Task.FromResult(
            Json(HttpStatusCode.BadRequest, new { status = 400, message = "Invalid refresh token" })));
        using var auth = CreateAuth(temp.Path, handler, out var host);

        Assert.Null(await auth.GetAccessTokenAsync());
        Assert.False(await auth.RefreshAccessTokenAsync());

        Assert.True(auth.LoginExpired);
        Assert.False(auth.CanRefresh);
        Assert.Null(auth.StoredToken);
        Assert.Equal(1, host.WarningToasts);
    }

    [Fact]
    public async Task NetworkFailureKeepsTheLoginForALaterRetry()
    {
        using var temp = new TwitchEventSubTests.TemporaryDirectory();
        WriteAuthState(temp.Path, "access-old", "refresh-old", DateTime.UtcNow.AddMinutes(5));
        var handler = new FakeTwitch(_ => throw new HttpRequestException("offline"));
        using var auth = CreateAuth(temp.Path, handler, out var host);

        Assert.Equal("access-old", await auth.GetAccessTokenAsync());

        Assert.False(auth.LoginExpired);
        Assert.True(auth.CanRefresh);
        Assert.Equal(0, host.WarningToasts);
    }

    private static TwitchAuthService CreateAuth(string path, HttpMessageHandler handler, out TwitchEventSubTests.HostProxy host)
    {
        var proxy = DispatchProxy.Create<IPluginHost, TwitchEventSubTests.HostProxy>();
        host = (TwitchEventSubTests.HostProxy)proxy;
        host.DataPath = path;
        return new TwitchAuthService(proxy, handler);
    }

    private static void WriteAuthState(string path, string accessToken, string refreshToken, DateTime expiresAtUtc) =>
        File.WriteAllText(Path.Combine(path, "twitch-auth.json"), JsonSerializer.Serialize(new
        {
            OAuthToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = expiresAtUtc,
        }));

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpRequestMessage request)
    {
        var body = await request.Content!.ReadAsStringAsync();
        return body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(
            pair => WebUtility.UrlDecode(pair[0]),
            pair => WebUtility.UrlDecode(pair[1]));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
    };

    private sealed class FakeTwitch(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }
}
