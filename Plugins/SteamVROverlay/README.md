# SteamVROverlay Plugin

An in-VR wrist watch for MultiShock, styled after OVR Toolkit's: the time, your devices'
batteries, media and volume controls, and performance stats stacked as cards, with the
shocker controls one tap away. Flows can also pop up notifications in front of you.

The watch attaches to a controller and stays hidden until you turn that wrist toward your
face. Point your other controller at it and use the trigger to use its controls.

## Requirements

- SteamVR, already running. The plugin never launches SteamVR itself; it attaches when
  SteamVR comes up and reattaches after a SteamVR restart.
- Windows (`win-x64`).

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip to MultiShock's `Plugins` folder
3. Restart MultiShock

## Using the watch

- **Reveal it** by turning the wrist it is attached to toward your face.
- **Click** by pointing the other controller at the watch and pulling the trigger. A cursor
  dot follows your aim and the controller ticks as you move over a control.
- **Move it** by pointing the other controller at it and holding the grip. The panel follows
  that hand until you let go, and the new spot is saved to the placement settings. Turn off
  `Move the panel by grabbing it in VR` to have the grip ignored on the panel.
- **Move watch** at the bottom of the watch, like OVR Toolkit's pencil, makes the whole watch
  grabbable with the trigger or the grip, whatever that setting says, and stops its other
  controls responding so nothing is pressed while moving it. Tap **Done** to finish.

### Watch cards

From top to bottom:

- **Time** and date, following the Windows 12- or 24-hour setting.
- **Media:** whatever Windows is playing (the same session the media keys control), with
  artwork, title, artist, progress, and previous, play/pause, and next.
- **Volume and microphone:** the default output device's volume and mute, and a mute toggle
  for the default microphone.
- **Performance:** gauges for CPU and memory use, GPU frame time against the headset's frame
  budget, the running VR app's frame rate against the display's refresh rate, and the share of
  frames SteamVR had to reproject.
- **Shockers**, which opens the shocker controls.
- **Move watch** and **Batteries** buttons. Batteries shows or hides the pills below it and
  saves the choice, the same setting as `Device batteries`.
- **Batteries:** a pill for every SteamVR device that reports one: headset, controllers, and
  trackers. Charging devices show a bolt; low batteries turn amber at 20% and red at 10%.

The time, the Shockers card, and the Move watch and Batteries buttons are always shown; the
others can be turned off, and the watch shrinks to fit.

Other plugins can add their own cards, which appear above Shockers, and pages, whose buttons
appear below it. See [Adding to the watch from another plugin](#adding-to-the-watch-from-another-plugin).

### Shocker controls

- **Target shockers** by tapping their names. With nothing selected, actions apply to every
  loaded shocker.
- **Set intensity and duration** by dragging the sliders.
- **Fire** with Vibrate, Shock, or Beep. **STOP ALL** ends active commands.
- **Watch** at the top returns to the watch. After the wrist has been down for 10 seconds,
  raising it shows the watch again, like a smartwatch returning to its face.

## Configuration

Settings live under **Settings → Plugins → SteamVROverlay**.

### Placement

`Attach to` picks the left or right controller. Offsets are in left-controller space: `+X`
points right, `+Y` up, and `+Z` back toward your elbow. The right hand uses the mirror image,
so the same values place the panel on either wrist. The defaults (`X -6 cm`, `Z 12 cm`,
`yaw -90°`, 12 cm wide) put it on the back of the wrist, clear of the controller, facing outward
like a watch face.
Comfortable values vary a lot between headsets, so adjust these while SteamVR is running and
watch the panel move, or move the panel in VR with the grip and let the values follow.
`Reset placement` puts it back in the default spot, for when it has been moved somewhere it
can no longer be seen or grabbed.

`Curvature` bends the panel toward you, which helps at larger widths.

The width applies to both the watch and the shocker controls. The watch is taller than it is
wide, and its height depends on the cards shown.

### Watch

Turns the batteries, media, volume and microphone, and performance cards on or off, along with
each card and page other plugins add.

### Visibility

With `Only show when you turn your wrist toward you` enabled, the panel appears when the
angle between its face and your headset falls within the activation angle. `Hide delay`
keeps it up briefly after you turn away so small wrist movements do not dismiss it; a click
already in progress also holds it open.

Disable gaze activation to pin the panel permanently to the controller.

### Limits and safety

`Maximum intensity` and `Maximum duration` cap what the in-VR sliders can reach, independent
of what the rest of MultiShock allows.

`Require press-and-hold on the shock button` guards against a stray laser click. The button
fills as a commit bar while held and cancels if you release early or slide off it. Turn it
off for a single-press shock button.

`Send a warning vibration before each shock` prefixes every shock with a warning command.

## Flow nodes

| Node | Kind | Purpose |
| --- | --- | --- |
| Wrist Overlay Action | Trigger | Fires when an action is used on the panel. Outputs the command, intensity, duration, and the device and shocker IDs it targeted. Can be filtered to one command. |
| Set Wrist Overlay Visibility | Action | Forces the panel shown or hidden, or restores automatic wrist-gaze behaviour. |
| Show VR Notification | Action | Pops up a title and message in front of you for a few seconds, optionally buzzing the controller that wears the panel. Fails with an error when SteamVR is not connected. |

## Adding to the watch from another plugin

A plugin adds cards and pages by implementing `IVrWatchProvider` from the plugin SDK
(`MultiShock.PluginSdk.VrWatch`) on its plugin class; it needs no reference to this plugin.
The contract lives in the SDK because it is the only assembly every plugin shares, so the SDK's
README has the full example.

- `GetWatchContent()` returns the plugin's cards and pages, built from text, stats, meters,
  buttons, toggles, and sliders. The watch draws them itself, so they match the built-in cards.
- `OnWatchInputAsync()` receives button presses, toggle changes, and slider releases, one at a
  time per plugin, off the overlay thread. A returned message shows briefly on the watch.
- A plugin whose content throws is left off the watch, and a failed input shows
  "*Plugin* could not do that"; neither affects the rest of the watch.
- The **Plugin page** picker on this plugin's page previews another plugin's page without a
  headset.

## How it works

The panel is drawn with SkiaSharp into an RGBA bitmap, redrawn only when something changes,
and uploaded into one of two alternating Direct3D 11 textures that are handed to SteamVR with
`IVROverlay::SetOverlayTexture`. (`SetOverlayRaw` recreates the texture on every call and makes
the panel flash while you interact with it.) Placement uses
`SetOverlayTransformTrackedDeviceRelative` against the controller's tracked device index.
SteamVR takes the overlay's shape from its texture, so the bitmap and textures are resized
whenever the view or the set of cards changes height; the gaps between the watch's cards are
transparent.

The overlay is deliberately not a SteamVR interactive overlay: that mode captures input from
the game and makes SteamVR draw every tracked device while it is visible. Instead the plugin
registers its own SteamVR Input action set (written to the plugin's data folder as
`input/actions.json`), reads the other hand's `pose/tip`, trigger, and grip, intersects that ray
with the panel itself, and draws its own cursor with a haptic tick on hover and click. The game
keeps receiving input throughout, so a trigger pull or grip on the panel also reaches the game.
While the panel is grabbed it keeps its pose relative to the grabbing hand; on release, its
position relative to the wearing controller is turned back into offsets and angles and saved.

Notifications use a second overlay locked to the headset, faded in and out and shown one at
a time.

The watch cards read their data from:

- **Batteries:** OpenVR device properties, polled every few seconds.
- **Performance:** the compositor's frame timings for the focused app, plus Windows' system
  times and memory status.
- **Media:** Windows' global media session API (`GlobalSystemMediaTransportControlsSessionManager`).
- **Volume and microphone:** Core Audio endpoints through NAudio, re-resolved every second so
  the controls follow SteamVR when it switches the default devices.
- **Other plugins' cards and pages:** each `IVrWatchProvider` among the loaded plugins, read
  twice a second while the watch is shown and again once a plugin has handled an input.
  Toggles and sliders show the value sent until then, so they do not flick back.

## Development

### Build from source

From repo root:
```powershell
# Debug build
.\scripts\build-plugins.ps1 -Plugin SteamVROverlay -Configuration Debug

# Release build
.\scripts\build-plugins.ps1 -Plugin SteamVROverlay -Configuration Release
```

### Tests

```powershell
dotnet test .\Tests\SteamVROverlay.Tests
```

The tests cover the watch layout, hit testing, selection, slider ranges, the hold-to-shock
guard, the media and audio controls, battery naming, frame-rate maths, the OpenVR pose maths,
other plugins' cards, pages, and controls, and rendering every view — the parts that are otherwise only observable inside a headset.

### Create release archive

```powershell
.\scripts\release-plugins.ps1 -Plugin SteamVROverlay
```

## Project Structure

```
SteamVROverlay/
├── Plugin.cs                       # Entry point, DI, navigation, flow nodes
├── HomePage.razor                  # Status page with a desktop preview of the panel
├── Components/Config/              # Settings UI
├── Nodes/                          # Flow nodes
├── Overlay/
│   ├── OverlayPanel.cs             # Panel state, watch cards, layout, hit testing
│   ├── PanelModels.cs              # Media, audio, battery, and stats snapshots
│   ├── WatchContributions.cs       # Other plugins' cards and pages: keys, checks, comparison
│   ├── OverlayRenderer*.cs         # Skia drawing: shocker controls, the watch, plugin widgets
│   ├── OverlayIcons.cs             # Lucide icons as Skia paths
│   ├── OverlayText.cs              # Text truncation and wrapping
│   ├── OverlayTheme.cs             # MultiShock design tokens for Skia
│   ├── OverlayMath.cs              # OpenVR pose conversions and ray hits
│   ├── OverlayTexture.cs           # Direct3D 11 texture hand-off
│   ├── NotificationOverlay.cs      # Head-locked notification popup
│   ├── NotificationRenderer.cs     # Notification drawing
│   ├── VrStatus.cs                 # Device batteries and frame timing from OpenVR
│   └── WristInput.cs               # SteamVR Input actions for the pointing hand
└── Services/
    ├── OverlaySettingsService.cs   # Persisted settings
    ├── MediaSessionService.cs      # Windows media session
    ├── SystemAudioService.cs       # Default output and microphone volume and mute
    ├── SystemUsageSampler.cs       # CPU and memory use
    ├── WatchProviderService.cs     # Reads other plugins' watch content, delivers their inputs
    └── WristOverlayService.cs      # OpenVR lifecycle, overlay loop, input, actions
```
