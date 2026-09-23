# WebSocket Control

This plugin lets another application send commands to MultiShock v4 over a local WebSocket. Install `WebSocketControl.msplugin`, then open **WebSocket Control** in MultiShock. The server starts on `ws://localhost:8765/` by default. Reveal the generated authentication key on that page and use it in each message. The key is saved in the plugin's private data directory.

The page lets you disable the server, change its port or key, and see its status and connected client count. Save settings to apply a change; active clients disconnect when the server restarts. The server accepts local connections only.

## Commands

Send UTF-8 JSON text frames. The same `cmd`, `value`, and `auth_key` envelope used by the v3 external connector is supported. A JSON string containing that object is also accepted for older clients.

List loaded shockers:

```json
{"cmd":"get_devices","auth_key":"YOUR_KEY"}
```

The response remains a plain JSON array:

```json
[{"name":"My device","id":123,"shockers":[{"name":"Left","identifier":456}]}]
```

Operate on loaded shockers:

```json
{"cmd":"operate","auth_key":"YOUR_KEY","value":{"action":"vibrate","intensity":25,"duration":0.5,"device_ids":[123],"shocker_ids":[456],"shocker_option":"all","warning":false,"held":false,"priority":false}}
```

`action` is `shock`, `vibrate`, `beep`, `end`, or `end_now`. Intensity defaults to `1` and must be `1`–`100` for active actions. Duration defaults to `0.1` seconds and must be greater than zero and at most 15 seconds. For compatibility, a `duration` value greater than 15 is interpreted as milliseconds. New clients can instead provide `duration_ms` for an unambiguous millisecond value. `shocker_option` is `all` (default) or `random`. `device_ids` and `shocker_ids` may contain integers or decimal strings; the two lists are combined as a union. Omitting both targets all loaded shockers. The flags default to `false`.

As before, successful `operate` commands have no response, and `get_devices` returns the array directly. An invalid request now receives a JSON error frame, for example:

```json
{"cmd":"error","value":{"code":"no_targets","message":"No loaded shockers match the requested IDs."}}
```

## Differences from MultiShock v3

| Behavior | MultiShock v4 plugin |
| --- | --- |
| Authentication | Generates a strong per-installation key. The v3 key is not imported; update each client with the key shown in the plugin page. Empty keys are rejected. |
| Listener | Uses `localhost:8765` by default and keeps the configured port. V3 searched ports 8765–8769 if 8765 was occupied; v4 shows a start error so clients do not silently follow a different port. |
| Enabled switch | Disabling the plugin stops the listener. V3 kept a listener open and ignored normal commands while disabled. |
| `get_devices` | Same array shape and field names, but returns loaded v4 devices and shockers. V3 included configured devices even when their shockers were not loaded. |
| `group_ids` | Nonempty lists return `unsupported_groups`. The v4 plugin SDK does not expose v3's user groups; use `device_ids` or `shocker_ids`. No action is sent for an unsupported group request. |
| `end_now` | Stops the selected output immediately, the same as `end`. V3 parsed `end_now` but its device action enum rejected it. |
| Request limits | Active actions require intensity 1–100 and duration at most 15 seconds. Malformed values are rejected before reaching the device manager. |
| Errors | Invalid JSON, wrong keys, unknown actions, bad IDs, unsupported groups, and missing targets receive an error frame instead of being silently ignored. |
| Internal add-ons | V3's special `identify`, Twitch event, and OSC `liindy` messages are not part of this plugin. V4 has separate Twitch and VRChat OSC plugins; clients using those special messages need to integrate with the corresponding v4 plugin. |

The server limits a message to 64 KiB and allows up to 16 simultaneous clients. Device actions still go through MultiShock's v4 device manager, so configured capability, duration, and cooldown limits apply. The `operate` command confirms that a request was accepted for dispatch; it does not confirm that a physical shocker executed it.
