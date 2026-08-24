# Changelog

## [0.1.0] - 2026-07-13

### Features

- Initial release of TikTok Integration plugin
- Connect to any TikTok LIVE room by `@username` (no OAuth; uses the public webcast feed)
- Gift Brackets: map gift coin value to intensity and duration, with streak/combo handling
- Individual Gifts: per-gift rules with their own intensity, duration, command, and shockers
- Coin Goal: cumulative meter that fires an action every time accumulated gift coins hit a goal
- Follows, Shares, Subscriptions, Viewer Joins, and Like-threshold actions
- Flow trigger nodes for gifts, gift brackets, individual gifts, chat, likes, follows, shares,
  joins, subscribes, viewer count, and stream end
- Request signing via Euler Stream (anonymous by default) with an optional API key, custom
  sign server, and session id
- Wait-for-live retry and automatic reconnect
