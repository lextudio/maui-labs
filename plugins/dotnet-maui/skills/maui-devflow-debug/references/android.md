# Android Reference

Prefer the unified `maui` CLI for Android device prep and DevFlow workflows.
Raw `adb` commands are kept only for operations that the `maui` CLI does not
yet wrap; those are grouped under
[Raw fallbacks not yet in `maui` CLI](#raw-fallbacks-not-yet-in-maui-cli).

## Table of Contents
- [Emulator Management](#emulator-management)
- [Building and Deploying](#building-and-deploying)
- [SDK and JDK Management](#sdk-and-jdk-management)
- [Raw fallbacks not yet in `maui` CLI](#raw-fallbacks-not-yet-in-maui-cli)
- [Troubleshooting](#troubleshooting)

## Emulator Management

### Avoiding multi-project conflicts

When multiple projects (or AI agents) may deploy to Android emulators
simultaneously, each project should use its own dedicated AVD. Two apps
deployed to the same emulator will coexist (unlike iOS), but `adb reverse` /
`adb forward` port forwarding is per-device and can cause confusion when
multiple emulators are running.

**Before creating or starting an emulator, check what's already in use:**
```bash
maui devflow list --json                          # agents with platform + port
maui device list --platform android --json        # connected emulators / devices
```

If an emulator is already running another project's agent, create a new AVD:
```bash
maui android emulator create "ProjectName-Pixel8" \
  --package "system-images;android-35;google_apis;arm64-v8a" \
  --device pixel_8
maui android emulator start "ProjectName-Pixel8"
```

**When multiple emulators are running**, target a specific serial with raw
`adb -s <serial>` for port forwarding (see fallbacks below).

**Naming convention:** Use `<ProjectName>-<DeviceType>` (e.g. `TodoApp-Pixel8`)
so it's clear which AVD belongs to which project.

### List, create, start, stop, delete AVDs
```bash
maui android emulator list                       # available AVDs
maui android emulator create <name> --package <system-image> --device <device>
maui android emulator start <name>               # add --cold-boot --wait if needed
maui android emulator stop <name>
maui android emulator delete <name>
```

All of the above accept `--json` for machine-readable output. The emulator
`create` command will prompt interactively for system image / device profile
when run without `--package` / `--device`.

### Verify emulator is running
```bash
maui device list --platform android --json
```

## Building and Deploying

```bash
# Build and deploy to running emulator
dotnet build -f net10.0-android -t:Run

# Build only (no deploy)
dotnet build -f net10.0-android
```

**Critical: Port forwarding after deploy** — the Android emulator runs in its
own network. Both directions need forwarding:

```bash
# Not yet wrapped by 'maui' CLI — use raw adb
adb reverse tcp:19223 tcp:19223                  # Broker (lets agent register)
adb forward tcp:<port> tcp:<port>                # Agent (lets CLI reach agent)
```

The broker `reverse` is needed so the agent inside the emulator can connect to
the host's broker daemon. The agent `forward` uses the port shown in
`maui devflow list` after the agent registers (range 10223–10899).

If the broker isn't available (fallback mode), forward the port from
`.mauidevflow` instead:
```bash
# Not yet wrapped by 'maui' CLI — use raw adb
adb forward tcp:9223 tcp:9223                    # Fallback: direct agent port
```

Then verify: `maui devflow ui status` and `maui devflow webview status`.

## SDK and JDK Management

### SDK
```bash
maui android sdk check --json                    # status (also reports SDK path)
maui android sdk list --json                     # installed packages
maui android sdk list --available --json         # all available packages
maui android sdk list --all --json               # both installed and available
maui android sdk install "platforms;android-35"
maui android sdk install "system-images;android-35;google_apis;arm64-v8a"
maui android sdk install "emulator"
maui android sdk install "platform-tools"
maui android sdk uninstall <package-name>
maui android sdk accept-licenses
```

`install` and `uninstall` accept package names as **positional** arguments
(no `--package` flag); pass multiple packages by space-separating them.

For a guided full-stack setup, run:
```bash
maui android install                             # interactive: platform + scope
```

### Typical setup for MAUI Android development
```bash
maui android sdk accept-licenses
maui android sdk install "platforms;android-35"
maui android sdk install "build-tools;35.0.0"
maui android sdk install "system-images;android-35;google_apis;arm64-v8a"
maui android sdk install "emulator"
maui android sdk install "platform-tools"
```

### JDK
```bash
maui android jdk check --json                    # current JDK status (path, version)
maui android jdk install --version 17            # install OpenJDK 17 or 21
maui android jdk list --json                     # available JDKs
```

### Environment variables
```bash
export ANDROID_HOME=$HOME/Library/Android/sdk
export ANDROID_SDK_ROOT=$ANDROID_HOME
export PATH=$PATH:$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator
```

## Raw fallbacks not yet in `maui` CLI

These operations are not wrapped by the `maui` CLI today. Use the raw tool and
prefer `maui` for everything else.

### Port forwarding (critical for MAUI DevFlow)
```bash
adb reverse tcp:19223 tcp:19223                  # Broker (agent → host)
adb forward tcp:<port> tcp:<port>                # Agent (CLI → emulator)
adb reverse --list
adb forward --list
adb reverse --remove-all
adb forward --remove-all

# Target a specific serial when multiple emulators are running:
adb -s emulator-5554 reverse tcp:19223 tcp:19223
adb -s emulator-5556 reverse tcp:19223 tcp:19223
```

### Install / uninstall / launch APK
```bash
adb install -r path/to/app.apk
adb uninstall <pkg>
adb shell am start -n <pkg>/<activity>
adb shell am force-stop <pkg>
adb shell pm list packages | grep <name>
```

### Logs
Once a DevFlow agent is connected, prefer in-app logs:
```bash
maui devflow logs --limit 50
```
Pre-agent or for system-level traces, use `adb logcat`:
```bash
adb logcat -s "DOTNET" --format brief             # .NET runtime logs
adb logcat -s "MauiDevFlow"                       # agent logs
adb logcat --pid=$(adb shell pidof <pkg>)         # app-specific logs
adb logcat -c                                     # clear log buffer
```

### Screenshots and screen recording
For a running MAUI app, prefer:
```bash
maui devflow ui screenshot --output screen.png
```
For pre-launch / system-level capture:
```bash
adb shell screencap /sdcard/screen.png && adb pull /sdcard/screen.png
adb shell screenrecord /sdcard/video.mp4          # Ctrl+C to stop
```

### File operations / shell
```bash
adb push local/file /sdcard/path
adb pull /sdcard/path local/file
adb -s <serial> shell
```

## Troubleshooting

- **`maui device list --platform android` shows "unauthorized"**: Accept the
  USB debugging prompt on the device/emulator.
- **Agent not connecting on emulator**: Forgot `adb reverse tcp:19223 tcp:19223`
  for the broker. Run port forwarding, then check `maui devflow list`.
- **Emulator won't start**: Check installed system images with
  `maui android sdk list`. Install one with
  `maui android sdk install "system-images;android-XX;..."`.
- **Build error "No Android devices found"**: Ensure emulator is booted
  (`maui device list --platform android`).
- **Slow emulator**: Use hardware acceleration. Prefer `arm64-v8a` images on
  Apple Silicon Macs.
- **JSON error envelope**: When a `maui` command fails with `--json`, parse
  the top-level `code` and `remediation` fields (no `error` wrapper; see
  `troubleshooting.md`). Common Android codes: `E2101` AndroidSdkNotFound,
  `E2103` AndroidLicensesNotAccepted, `E2106` AndroidEmulatorNotFound,
  `E2110` AndroidAdbNotFound, `E2111` AndroidDeviceNotFound.
