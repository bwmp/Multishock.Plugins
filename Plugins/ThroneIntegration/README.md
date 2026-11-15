# Throne Integration Plugin

Adds a **Throne Gift Received** flow trigger that fires when someone buys a gift from your [Throne](https://throne.com) wishlist or contributes to a crowdfunded gift.

## How it works

Throne delivers each purchase to a webhook URL. MultiShock can't receive webhooks directly, so it goes through the Throne relay at `throne.bwmp.dev`:

1. MultiShock creates a private connection on the relay. The relay gives back a webhook URL.
2. You paste that URL into Throne.
3. Throne sends each gift to the relay. The relay checks Throne's signature and stores the gift.
4. MultiShock collects stored gifts from the relay and fires your flows.

You don't sign in to Throne, and you don't need to open ports or run a tunnel. The relay keeps gifts for seven days, so gifts sent while MultiShock is closed still run your flows the next time you open it.

## Setup

1. Install the plugin and open **Throne** in the MultiShock sidebar.
2. Click **Create webhook URL**.
3. Click the copy button next to the webhook URL.
4. On Throne, open your creator settings, go to **Integrations** and turn on **Webhooks**. Paste the URL as the subscriber URL and save.
5. Add the **Throne Gift Received** trigger to a flow and connect it to the actions you want.

The status reads **Waiting for first gift** until Throne delivers something. It changes to **Receiving gifts** when the first gift or contribution arrives, and the page then shows the Throne account the connection is linked to. The relay only accepts deliveries signed by Throne, so a real purchase is the way to confirm the whole path.

Keep the webhook URL private, and don't show it on stream. The page hides it unless you click the eye button.

MultiShock reconnects to the relay on its own when it starts, and after network drops. **Disconnect** pauses collection; gifts keep arriving at the relay and run your flows when you **Connect** again, as long as that's within seven days.

## Flow trigger

**Throne Gift Received** outputs:

| Output | Type | Notes |
|--------|------|-------|
| Triggered | Flow | |
| Gifter Username | String | `Anonymous` for anonymous gifts. |
| Message | String | Empty when the gifter left no message. |
| Event Type | String | `gift_purchased`, `contribution_purchased` or `gift_crowdfunded`. |
| Item Name | String | |
| Item Image URL | String | The item's thumbnail. |
| Amount | Number | In the gift's currency, e.g. `10.99`. A contribution reports the amount contributed; a purchase reports the item price. |
| Currency | String | ISO 4217 code such as `USD` or `EUR`. Amounts are not converted. |
| Surprise Gift | Boolean | Set for surprise gifts. |
| Timestamp | Number | When the relay received the gift, in Unix milliseconds. |

Properties:

- **Minimum Amount**: only fire for gifts worth at least this much, compared in the gift's own currency.
- **Fire on crowdfund completion**: off by default. When a crowdfunded gift is fully funded, Throne sends a completion event with the full item price. Each contribution has already fired the trigger, so turning this on counts the gift twice. Turn it on only if you want a separate reaction to completion, and branch on **Event Type**.

## Troubleshooting

- **Waiting for first gift doesn't change after a purchase.** Check that Webhooks is turned on in Throne's Integrations and that the subscriber URL matches the one MultiShock shows.
- **Connection no longer valid.** The relay no longer recognizes the connection. This happens if it was removed, or if no gift arrived within seven days of creating it. Click **Remove connection**, create a new URL, and replace the old one in Throne.
- **Some gifts were missed.** MultiShock was closed for longer than the relay keeps gifts. Those gifts never reached MultiShock and their flows did not run. Click **Skip and resume** to continue with the gifts the relay still has.
- **Reconnecting.** MultiShock can't reach the relay and retries with increasing delays, up to a minute apart. Gifts are not lost while it retries.

Each gift runs its flows once. If MultiShock crashes while a gift's flow is starting, that gift can run again when MultiShock restarts.

## Removing the connection

**Remove connection** revokes the connection on the relay and deletes any gifts still waiting there. Remove the webhook URL from Throne's Webhooks settings too.

## Privacy

The relay stores gift details (usernames, item names, messages and amounts) for up to seven days. The client token and webhook URL are kept in your operating system's credential store (encrypted for your Windows user, or in the Linux keyring). The connection ID, the event cursor and the IDs of handled gifts are kept in `throne-config.json` in the plugin's data folder. If MultiShock can't use secure storage, for example on Linux without a running keyring, the plugin page says so and the credentials are kept in `plaintext-secrets.json` in that folder until it can; don't share that file.

## Upgrading from 1.3.0 or earlier

Earlier versions watched a Throne username through a different API, which no longer exists. After updating, set up the webhook as described above; the saved username is no longer used. Existing flows keep working, with two changes to the trigger's outputs:

- **Event Type** (previously **Gift Type**) now reports `gift_purchased`, `contribution_purchased` or `gift_crowdfunded`.
- **Minimum Amount** is now applied. In earlier versions it had no effect.

## Development

From the repository root:

```powershell
# Debug build
dotnet build Plugins/ThroneIntegration/ThroneIntegration.csproj

# Release package (.msplugin)
.\scripts\release-plugins.ps1 -Plugin ThroneIntegration
```

The relay's API and hosting notes are in [bwmp/Throne-API](https://github.com/bwmp/Throne-API) (`CLIENT.md`). The plugin uses its `ThroneAPI.Client` package.

```text
ThroneIntegration/
├── Plugin.cs                          # Plugin entry point and service registration
├── Components/Pages/HomePage.razor    # Setup, status and recent gifts
├── Models/ThroneGift.cs               # Relay event mapped for flows and the UI
├── Nodes/ThroneGiftTriggerNode.cs     # Flow trigger
└── Services/
    ├── ThroneService.cs               # Relay connection and event polling
    ├── ThroneSettingsService.cs       # Credentials, cursor and handled event IDs
    └── ThroneTriggerManager.cs        # Fires registered trigger nodes
```
