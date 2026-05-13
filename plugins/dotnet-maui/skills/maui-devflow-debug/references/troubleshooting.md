# Troubleshooting

## Table of Contents
- [Machine-readable output and error envelope](#machine-readable-output-and-error-envelope)
- [Connection Refused](#connection-refused--cannot-connect)
- [Android UI Thread Exceptions](#android-ui-thread-exceptions)
- [Build Failures](#build-failures)
- [CDP Not Connecting](#cdp-not-connecting-blazor-hybrid)
- [Mac Catalyst Permission Dialogs](#mac-catalyst-repeated-permission-dialogs-on-rebuild)

## Machine-readable output and error envelope <a name="machine-readable-output-and-error-envelope"></a>

Always pass `--json` to any `maui` command an agent will parse, and `--ci` for
non-interactive failure-fast runs.

The full `--json` error envelope contract (schema, code categories, worked examples, PowerShell and Bash consumers) is documented in
[`src/Cli/README.md` — Error envelope](../../../../../src/Cli/README.md#error-envelope).

**Quick reference** — when a non-DevFlow command fails with `--json`, stdout is a top-level JSON object (no `"error"` wrapper). Note: `maui devflow ...` uses a different JSON error shape and writes errors to stderr.

```json
{
  "code": "E2103",
  "category": "platform",
  "severity": "error",
  "message": "Android SDK licenses have not been accepted.",
  "remediation": {
    "type": "autofixable",
    "command": "maui android sdk accept-licenses"
  }
}
```

Optional fields (`remediation`, `context`, `native_error`, `docs_url`, `correlation_id`) are **omitted entirely** when null.
When `remediation.type` is `autofixable`, run `remediation.command` then retry the original command.
When `remediation` is absent, surface `message` and stop retrying.

### Code categories

| Prefix | Category | Examples |
|--------|----------|----------|
| `E1xxx` | Tool error (likely an internal bug) | `E1001` InternalError, `E1004` InvalidArgument, `E1006` DeviceNotFound, `E1007` PlatformNotSupported |
| `E2xxx` | Platform / SDK | `E2001` JdkNotFound, `E2101` AndroidSdkNotFound, `E2103` AndroidLicensesNotAccepted, `E2106` AndroidEmulatorNotFound, `E2110` AndroidAdbNotFound, `E2201` AppleXcodeNotFound, `E2204` AppleSimulatorNotFound, `E2402` MauiWorkloadMissing |
| `E3xxx` | User action required | (e.g., choose a target when multiple match) |
| `E4xxx` | Network | (download / fetch failures) |
| `E5xxx` | Permission | (sandbox / elevation issues) |

### Remediation

`remediation.type` is one of:

- `autofixable` — run `remediation.command` and retry the original command.
- `useraction` — present `remediation.manual_steps` to the user.
- `terminal` — cannot be fixed (e.g., unsupported OS); abort.
- `unknown` — fall back to displaying `message`.

### Worked example

```bash
maui android emulator start Pixel8 --json
# → { "code": "E2106", "remediation": { "type": "autofixable", "command": "maui android emulator create Pixel8 --package ..." } }

# Auto-fix path:
maui android emulator create Pixel8 --package "system-images;android-35;google_apis;arm64-v8a" --device pixel_8 --json
maui android emulator start Pixel8 --json   # retry original
```

## Connection Refused / Cannot Connect

If `maui devflow ui status` fails with connection refused:

1. **App not running?** Verify the app launched: check the build output for errors.
2. **Run the diagnostic first:** `maui devflow diagnose` separates broker
   startup, project integration, no running app, and target-device networking.
3. **Check the broker:** Run `maui devflow list` to see if the agent registered. If the list
   is empty, the app may not have connected to the broker yet (wait a few seconds and retry).
4. **Wrong port?** If using `.mauidevflow`, ensure the port matches between build and CLI.
   Run CLI from the project directory so it auto-detects the config file.
5. **Port already in use?** Another process may hold the port. Check with:
   ```bash
   # Not yet wrapped by 'maui' CLI — use raw lsof
   lsof -i :<port>       # macOS/Linux
   ```
   With the broker, this is less common since ports are auto-assigned.
6. **Android?** Did you run `adb reverse tcp:19223 tcp:19223` (for broker) and
   `adb forward tcp:<port> tcp:<port>` (for agent)? Re-run after each deploy.
   If broker/list is empty, still try direct status:
   ```bash
   adb devices
   adb forward tcp:9223 tcp:9223
   maui devflow agent status --agent-host localhost --agent-port 9223
   ```
   (Port forwarding is not yet wrapped by `maui` — use raw `adb`.)
7. **Mac Catalyst?** Check entitlements include `network.server` (see setup.md step 5).
8. **macOS (AppKit)?** Ensure `AddMacOSEssentials()` is called and the app window appeared.
   See [references/macos.md](macos.md) for troubleshooting.
9. **Linux/GTK?** No special network setup needed — runs directly on localhost. Check if the app started successfully.
10. **Broker issues?** `maui devflow broker status` to check. `maui devflow broker stop` then
    retry (CLI will auto-restart it).

## Android UI Thread Exceptions

If `maui devflow ui tap`, `fill`, `focus`, or other UI actions fail on Android
with `CalledFromWrongThreadException`, treat it as likely DevFlow agent action
dispatch trouble rather than an app logic bug, especially when manual input or
ADB taps work.

Capture evidence before changing app code:

```bash
maui devflow agent status --agent-host localhost --agent-port <port>
maui devflow ui query --automationId <control-id> --agent-host localhost --agent-port <port>
adb logcat -d -t 300 | grep -i "CalledFromWrongThreadException\\|DevFlow\\|DOTNET"
```

Report the DevFlow command, target platform, agent version from `agent status`,
the queried element id/AutomationId, and the logcat exception. Do not work
around this with coordinate-only automation unless you only need a temporary
validation fallback.

## Build Failures

**Missing workloads:**
```
error NETSDK1147: To build this project, the following workloads must be installed: maui-ios
```
Fix: `dotnet workload install maui` (installs all MAUI workloads).
Error code via `maui` JSON: `E2402` MauiWorkloadMissing (often `autofixable`).

**SDK version mismatch:**
```
error : The current .NET SDK does not support targeting .NET 10.0
```
Fix: Install the required .NET SDK version, or check `global.json` for version pins.

**Android SDK not found:**
```
error XA0000: Could not find Android SDK
```
Fix: Install Android SDK via `maui android sdk install "platforms;android-35"`
(or run `maui android install` for guided setup), or set `$ANDROID_HOME`.
Error code via `maui` JSON: `E2101` AndroidSdkNotFound.

**iOS provisioning / signing errors:**
Fix: For simulators, ensure no signing is configured (default). For devices, set up provisioning
profiles via your Apple Developer account.

**General build failure recovery:**
1. `dotnet clean` then retry the build
2. Delete `bin/` and `obj/` directories: `rm -rf bin obj` then rebuild
3. Check the full build output (not just the last error) — earlier warnings often reveal the root cause

## CDP Not Connecting (Blazor Hybrid)

If `maui devflow webview status` fails but `ui status` works:

1. **Chobitsu not loading?** Check logs for `[BlazorDevFlow]` messages. If auto-injection failed, add `<script src="chobitsu.js"></script>` manually to `wwwroot/index.html`
2. **Blazor not initialized?** Navigate to a Blazor page first, then retry
3. Check app logs: `maui devflow logs --limit 20` — look for `[BlazorDevFlow]` errors

## Mac Catalyst: Repeated Permission Dialogs on Rebuild

If macOS prompts "App would like to access your Documents folder" on every rebuild:

**Cause:** TCC permissions are tied to the app's code signature. Ad-hoc Debug builds produce a
different signature each rebuild → macOS forgets the grant and re-prompts. This happens even
with App Sandbox disabled.

**Fix:** Don't access TCC-protected directories (`~/Documents`, `~/Downloads`, `~/Desktop`,
or dotfiles like `~/.myapp/` in the home root) programmatically. Instead use:
- `Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)` → `~/Library/Application Support/` (not TCC-protected)
- `NSOpenPanel`/`NSSavePanel` for user-initiated file access (grants automatic TCC exemption)

If you can't avoid TCC paths, sign Debug builds with a stable Apple Development certificate
so the code signature stays consistent across rebuilds.

## macOS (AppKit) Issues

For detailed macOS (AppKit) troubleshooting, see [references/macos.md](macos.md#troubleshooting).

Common issues:
- **No window appears** → Missing `AddMacOSEssentials()` in builder
- **SIGKILL on launch** → Don't re-sign manually; clean rebuild instead
- **Blazor stuck on "Loading..."** → Use `MacOSBlazorWebView`, not standard `BlazorWebView`
- **No sidebar content** → Add `MacOSShell.SetUseNativeSidebar(shell, true)` + `FlyoutBehavior.Locked`