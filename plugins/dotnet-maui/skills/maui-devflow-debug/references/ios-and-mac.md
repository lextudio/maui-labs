# iOS & Mac Catalyst Reference

Prefer the unified `maui` CLI for simulator/runtime/Xcode discovery, full
lifecycle (create / erase / install / uninstall / launch / terminate), and
permission management (`maui devflow ui permission`). Raw `xcrun simctl` is
kept only for a few utilities not yet wrapped by the `maui` CLI (appearance /
openurl / push / location / addmedia / pbcopy / screenshots). Those are
grouped under
[Raw fallbacks not yet in `maui` CLI](#raw-fallbacks-not-yet-in-maui-cli).

The standalone `apple` command from `appledev.tools` covers some of the same
gaps and can be used interchangeably with `xcrun simctl`; install it only if
you cannot use raw `xcrun simctl`.

## Table of Contents
- [Simulator Management](#simulator-management)
- [Building and Deploying](#building-and-deploying)
- [Xcode and Runtime Discovery](#xcode-and-runtime-discovery)
- [Raw fallbacks not yet in `maui` CLI](#raw-fallbacks-not-yet-in-maui-cli)
- [Troubleshooting](#troubleshooting)
- [Permission & Dialog Handling](#permission--dialog-handling)
- [Dark Mode Testing](#dark-mode-testing)

## Simulator Management

### Avoiding multi-project conflicts

When multiple projects (or AI agents) may deploy to iOS simulators simultaneously, each
project should use its own dedicated simulator. Two apps deployed to the same simulator
will replace each other — only the last-deployed app survives.

**Before creating or booting a simulator, check what's already in use:**
```bash
maui devflow list --json                         # agents with platform + port
maui apple simulator list --json                 # all simulators
maui device list --platform apple --json         # cross-platform device discovery
```

If a booted simulator is already running another project's agent, create a new one:
```bash
maui apple simulator create "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro" \
  --name "ProjectName-iPhone17Pro" --runtime "com.apple.CoreSimulator.SimRuntime.iOS-26-2"
```

To avoid creating duplicates, add `--if-not-exists` — reuses an existing
simulator with the same name instead of failing.

**Naming convention:** Use `<ProjectName>-<DeviceType>` (e.g. `TodoApp-iPhone17Pro`) so
it's clear which simulator belongs to which project.

### List simulators
```bash
maui apple simulator list --json                 # formatted table or JSON
```

### Boot / shutdown / delete
```bash
maui apple simulator start <name-or-udid>
maui apple simulator stop <name-or-udid>         # accepts 'all' to stop everything
maui apple simulator delete <name-or-udid>
```

### Create / erase / install / launch / terminate
```bash
maui apple simulator create <device-type> --name <name> --runtime <runtime-id>
maui apple simulator erase <name-or-udid>        # factory reset
maui apple simulator install <udid> /path/to/App.app
maui apple simulator uninstall <udid> com.company.appid
maui apple simulator launch <udid> com.company.appid
maui apple simulator terminate <udid> com.company.appid
```

`device-type` is a positional arg (e.g. `com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro`).
Discover available types with `xcrun simctl list devicetypes` and runtimes
with `maui apple runtime list --json`.

### Screenshots (Mac Catalyst, macOS, iOS — when DevFlow agent is connected)

**Use `maui devflow ui screenshot`** for any running MAUI app — it captures the UI in-process
and does NOT require the app to be in the foreground (important for Mac Catalyst). Never use
`osascript` to bring the window to the front or `screencapture` for Mac Catalyst screenshots;
they are unnecessary and unreliable.

```bash
maui devflow ui screenshot --output screen.png
```

For pre-launch / system-level simulator captures, use raw `xcrun simctl` (see
[Raw fallbacks](#raw-fallbacks-not-yet-in-maui-cli)).

## Building and Deploying

### Mac Catalyst

**⚠️ Entitlements required:** Mac Catalyst apps are sandboxed by default and need the
`com.apple.security.network.server` entitlement for MAUI DevFlow's in-app HTTP server.
Without it, the agent fails to bind its port and the app may crash silently.

**Quick fix (disable sandbox for Debug):** Create `Platforms/MacCatalyst/Entitlements.Debug.plist`:
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>com.apple.security.app-sandbox</key>
    <false/>
    <key>com.apple.security.network.client</key>
    <true/>
  </dict>
</plist>
```

Then reference it in `.csproj` for Debug only:
```xml
<PropertyGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)'))
    == 'maccatalyst' and '$(Configuration)' == 'Debug'">
  <CodeSignEntitlements>Platforms/MacCatalyst/Entitlements.Debug.plist</CodeSignEntitlements>
</PropertyGroup>
```

If sandbox must stay enabled (e.g. testing App Store builds), add `network.server` explicitly.
See [setup.md Step 5](setup.md#5-mac-catalyst-entitlements) for the full sandbox-enabled plist.

```bash
dotnet build -f net10.0-maccatalyst                          # build only
dotnet build -f net10.0-maccatalyst -t:Run                   # build + run
open path/to/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/AppName.app  # run existing
```

### iOS Simulator
```bash
# Find UDID of booted simulator
UDID=$(xcrun simctl list devices booted -j | python3 -c "
import json,sys
d=json.load(sys.stdin)
for r in d['devices'].values():
  for dev in r:
    if dev['state']=='Booted': print(dev['udid']); break
" 2>/dev/null | head -1)

# Build and deploy
dotnet build -f net10.0-ios -t:Run -p:_DeviceName=:v2:udid=$UDID
```

The `-t:Run` target keeps the process alive while the app runs — it **blocks until the app exits**.
Always run in an async/background shell, then poll `maui devflow ui status` to detect when the
app is ready. Do NOT wait for the process to finish.

### Determining the correct TFM
Check the project file for `<TargetFrameworks>`:
```bash
grep -i TargetFramework *.csproj
```
Common values: `net9.0-ios`, `net9.0-maccatalyst`, `net10.0-ios`, `net10.0-maccatalyst`.

## Xcode and Runtime Discovery

```bash
maui apple xcode list --json                     # installed Xcode versions
maui apple runtime list --json                   # installed simulator runtimes
maui apple runtime list --platform ios --json    # filter by platform
```

All accept `--json` for machine-readable output.

## Raw fallbacks not yet in `maui` CLI

These operations are not wrapped by the `maui` CLI today. Use raw `xcrun
simctl` (preferred — built into Xcode) or the `apple` command from
`appledev.tools` if installed.

### System-level simulator screenshots
```bash
xcrun simctl io booted screenshot output.png
```

### Other simctl utilities
| Command | Use |
|---------|-----|
| `xcrun simctl addmedia <UDID> file.jpg` | Add photos/videos to sim |
| `xcrun simctl openurl <UDID> "url"` | Open URL / deep link |
| `xcrun simctl push <UDID> bundle payload.json` | Simulate push notification |
| `xcrun simctl location <UDID> set 37.33,-122.03` | Set GPS location |
| `xcrun simctl pbcopy <UDID>` / `pbpaste <UDID>` | Clipboard bridge |
| `xcrun simctl ui <UDID> appearance dark\|light` | Toggle dark mode |
| `xcrun simctl listapps <UDID>` | List all installed apps |
| `xcrun simctl get_app_container <UDID> bundle` | Get app container path |

## Troubleshooting

- **Mac Catalyst blank/white screen after crash**: macOS shows a "reopen windows" dialog after
  a crash, blocking the app from rendering. All MAUI elements appear as `[hidden] [disabled]`
  with `-1x-1` sizes. Fix: clear saved state before launch:
  ```bash
  rm -rf ~/Library/Saved\ Application\ State/<bundle-id>.savedState
  ```
  If the "Reopen windows?" dialog is already on screen, ask the user to dismiss it manually,
  then relaunch. Do not use AppleScript here by default — it steals focus from the user's
  desktop session.
- **"Unable to lookup in current state: Shutdown"**: Simulator not booted. Run
  `maui apple simulator start <UDID>`.
- **Build error NETSDK1005 "Assets file doesn't have a target"**: Wrong TFM. Check
  `<TargetFrameworks>` in .csproj and use matching version (e.g. `net10.0-ios` not `net9.0-ios`).
- **Agent not connecting after deploy**: The app may still be launching. Poll
  `maui devflow ui status` every few seconds. If it hasn't connected after ~60-90s, read the
  async shell output from `dotnet build -t:Run` for build/launch errors.
- **Mac Catalyst app name vs binary name**: The `.app` bundle name may differ from the project
  name (e.g. `MauiTodo.app` vs `SampleMauiApp`). Check the `ApplicationTitle` in .csproj.
  Find the bundle: `find bin/Debug/net10.0-maccatalyst -name "*.app" -maxdepth 3`

## Permission & Dialog Handling

### Pre-grant permissions (prevents dialogs from appearing)

Use the `maui devflow ui permission` command — it wraps `xcrun simctl
privacy` and auto-detects the booted simulator UDID and bundle id when the
DevFlow agent is connected:

```bash
maui devflow ui permission grant location  --bundle-id com.company.appid
maui devflow ui permission grant camera    --bundle-id com.company.appid
maui devflow ui permission grant photos    --bundle-id com.company.appid
maui devflow ui permission grant contacts  --bundle-id com.company.appid
maui devflow ui permission grant microphone --bundle-id com.company.appid

# Grant all permissions at once
maui devflow ui permission grant all --bundle-id com.company.appid

# Revoke (deny) a permission
maui devflow ui permission revoke location --bundle-id com.company.appid

# Reset (next request will show dialog again)
maui devflow ui permission reset all --bundle-id com.company.appid
```

`--udid <UDID>` is optional (auto-detects the booted simulator); `--bundle-id`
is required for per-app grants — omitting it applies the permission to all
apps on the simulator.

Available services (passed straight through to `simctl privacy`): `all`,
`calendar`, `contacts`, `contacts-limited`, `location`, `location-always`,
`photos`, `photos-add`, `media-library`, `microphone`, `motion`,
`reminders`, `siri`.

If the agent is not connected and you need raw access:

```bash
# Not yet covered by 'maui devflow ui permission' — use raw xcrun simctl
xcrun simctl privacy <UDID> grant all com.company.appid
xcrun simctl privacy <UDID> reset all com.company.appid
```

### Using MAUI DevFlow Driver for permissions
```csharp
var driver = new iOSSimulatorAppDriver();
driver.DeviceUdid = "<UDID>";
driver.BundleId = "com.company.appid";

// Pre-grant before running the app
await driver.GrantPermissionAsync(PermissionService.Location);
await driver.GrantPermissionAsync(PermissionService.Camera);

// Reset to test the dialog flow
await driver.ResetPermissionAsync(PermissionService.Location);
```

### Detecting and dismissing alerts (accessibility tree + HID tap)
When a dialog appears unexpectedly (permission prompt, app alert, action sheet), the driver can
detect it via the iOS accessibility tree and tap a button to dismiss it:

```csharp
// Check if an alert is currently showing
var alert = await driver.DetectAlertAsync();
if (alert is not null)
{
    Console.WriteLine($"Alert: {alert.Title}");
    foreach (var btn in alert.Buttons)
        Console.WriteLine($"  Button: {btn.Label} at ({btn.CenterX},{btn.CenterY})");
}

// Dismiss by tapping the first "accept" button (Allow, OK, etc.)
await driver.DismissAlertAsync();

// Dismiss by tapping a specific button
await driver.DismissAlertAsync("Don't Allow");

// Convenience: detect + dismiss if present, no-op if not
await driver.HandleAlertIfPresentAsync();
```

### Example workflow: permission dialog handling
```
1. App requests location → system shows "Allow location?" dialog
2. Agent detects dialog via DetectAlertAsync()
3. Agent sees buttons: ["Allow While Using App", "Allow Once", "Don't Allow"]
4. Agent taps "Allow While Using App" via DismissAlertAsync("Allow While Using App")
5. App receives permission grant, continues normal flow
```

### Dialog test page in SampleMauiApp
The SampleMauiApp includes a **Dialogs** tab with buttons that trigger:
- **Permission dialogs**: Location, Camera, Photos, Contacts, Microphone, Notifications
- **App alerts**: OK-only, OK/Cancel, custom buttons (Delete/Keep)
- **Action sheets**: Multiple options with cancel/destructive
- **Prompt dialogs**: Text input with OK/Cancel

Use these to test and validate dialog detection and dismissal workflows.

## Dark Mode Testing

### Toggle dark mode
```bash
# Preferred: use an in-app theme toggle so the host desktop is unaffected.
#
# iOS Simulator (safe: affects the simulator only) —
# not yet wrapped by 'maui' CLI, use raw xcrun simctl:
xcrun simctl ui <UDID> appearance dark
xcrun simctl ui <UDID> appearance light
```

For **Mac Catalyst**, changing system appearance affects the user's entire macOS desktop. Only do
that with explicit user approval; otherwise prefer app-level theme controls and verify via
`maui devflow ui property` / WebView inspection.

### Verify dark mode via inspection
Use `maui devflow` to verify colors without relying on screenshots:
```bash
maui devflow ui property <elementId> BackgroundColor   # check MAUI element colors
maui devflow webview Runtime evaluate "window.matchMedia('(prefers-color-scheme: dark)').matches"  # Blazor
```
