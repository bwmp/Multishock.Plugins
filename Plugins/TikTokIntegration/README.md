# TikTok Integration

Reacts to TikTok LIVE events (gifts, chat, likes, follows, shares, subscriptions, viewer
joins) by triggering shockers, and exposes those events as flow trigger nodes.

## How it works

TikTok does not offer an official LIVE API. This plugin connects to the same public
"webcast" feed that TikTok's own web player uses:

1. Resolve the streamer's `room_id` from their `/live` page (with an API fallback).
2. Fetch room info + the gift catalog.
3. Perform a **signed** `im/fetch/` request. TikTok requires signed parameters
   (`msToken`/`X-Bogus`) that cannot be produced client-side, so signing is delegated to a
   community sign server ([Euler Stream](https://www.eulerstream.com/) by default). An
   optional API key raises the signing rate limit.
4. Upgrade to the webcast websocket and decode the protobuf event stream
   (`Protocol/WebcastDecoder.cs` — a hand-rolled reader, no external protobuf dependency).

Everything is read-only. Connecting to LIVE data this way is against TikTok's Terms of
Service; TikTok occasionally changes the protocol, which may require a plugin update.

## Configuration pages

- **TikTok Integration** — connect by `@username`, auto-connect / wait-for-live /
  auto-reconnect toggles, optional Euler Stream key / custom sign server / session id, and
  the live event log.
- **Gift Brackets** — map gift **coin value** to intensity/duration (round-down or exact).
- **Individual Gifts** — per-gift rules with their own intensity, duration, command,
  shockers, and optional combo-scaled duration. Rules take priority over brackets.
- **Follows / Shares / Subs** — simple one-shot actions for follow, share, subscribe, join,
  and a like threshold.

## Gift streaks

Streakable gifts (e.g. Roses) send a message per combo tick plus a final message with the
total count. By default a combo resolves to a **single** action for the full coin value.
"Trigger on every combo tick" on the Gift Brackets page opts into per-tick actions instead.
