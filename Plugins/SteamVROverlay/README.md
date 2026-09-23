# SteamVROverlay Plugin

An in-VR wrist panel for controlling shockers without leaving your headset.

The panel attaches to a controller and stays hidden until you turn that wrist toward your
face, the way you would check a watch. Point your other controller at it and use the
trigger to pick shockers, set intensity and duration, and fire a vibrate, shock, or beep.

## Requirements

- SteamVR, already running. The plugin never launches SteamVR itself; it attaches when
  SteamVR comes up and reattaches after a SteamVR restart.
- Windows (`win-x64`).

## Installation

1. Download the latest release from the [Releases](../../releases) page
2. Extract the zip to MultiShock's `Plugins` folder
3. Restart MultiShock

## Using the panel

- **Reveal it** by turning the wrist it is attached to toward your face.
- **Click** by pointing the other controller at the panel and pulling the trigger. A cursor
  dot follows your aim and the controller ticks as you move over a control.
- **Target shockers** by tapping their names. With nothing selected, actions apply to every
  loaded shocker.
- **Set intensity and duration** by dragging the sliders.
- **Fire** with Vibrate, Shock, or Beep. **STOP ALL** ends active commands.

## Configuration

Settings live under **Settings → Plugins → SteamVROverlay**.

### Placement

`Attach to` picks the left or right controller. Offsets are in left-controller space: `+X`
points right, `+Y` up, and `+Z` back toward your elbow. The right hand uses the mirror image,
so the same values place the panel on either wrist. The defaults (`X -6 cm`, `Z 12 cm`,
`yaw -90°`, 12 cm wide) put it on the back of the wrist, clear of the controller, facing outward
like a watch face.
Comfortable values vary a lot between headsets, so adjust these while SteamVR is running and
watch the panel move.

`Curvature` bends the panel toward you, which helps at larger widths.

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

## How it works

The panel is drawn with SkiaSharp into an RGBA bitmap, redrawn only when something changes,
and uploaded into one of two alternating Direct3D 11 textures that are handed to SteamVR with
`IVROverlay::SetOverlayTexture`. (`SetOverlayRaw` recreates the texture on every call and makes
the panel flash while you interact with it.) Placement uses
`SetOverlayTransformTrackedDeviceRelative` against the controller's tracked device index.

The overlay is deliberately not a SteamVR interactive overlay: that mode captures input from
the game and makes SteamVR draw every tracked device while it is visible. Instead the plugin
registers its own SteamVR Input action set (written to the plugin's data folder as
`input/actions.json`), reads the other hand's `pose/tip` and trigger, intersects that ray with
the panel itself, and draws its own cursor with a haptic tick on hover and click. The game keeps
receiving input throughout, so a trigger pull on the panel also reaches the game.

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

The tests cover panel hit testing, selection, slider ranges, the hold-to-shock guard, and the
OpenVR pose maths — the parts that are otherwise only observable inside a headset.

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
│   ├── OverlayPanel.cs             # Panel state, layout, hit testing
│   ├── OverlayRenderer.cs          # Skia drawing
│   ├── OverlayTheme.cs             # MultiShock design tokens for Skia
│   ├── OverlayMath.cs              # OpenVR pose conversions and ray hits
│   ├── OverlayTexture.cs           # Direct3D 11 texture hand-off
│   └── WristInput.cs               # SteamVR Input actions for the pointing hand
└── Services/
    ├── OverlaySettingsService.cs   # Persisted settings
    └── WristOverlayService.cs      # OpenVR lifecycle, overlay loop, input, actions
```
