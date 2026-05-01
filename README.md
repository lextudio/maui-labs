# .NET MAUI Labs

Experimental packages and tooling for .NET MAUI. This repository hosts pre-release projects that are in active development and may ship independently.

> ⚠️ **These packages are experimental.** APIs may change between releases. These packages are not covered by the [.NET MAUI Support Policy](https://dotnet.microsoft.com/platform/support/policy/maui) and are provided as-is.

## Fastest install path

Install the unified `maui` CLI once, then let it guide the rest of your machine setup:

```bash
dotnet tool install -g Microsoft.Maui.Cli --prerelease
maui doctor
```

For Android development, the CLI can install and configure the JDK, Android SDK, licenses, and an emulator:

```bash
maui android install
maui device list
```

On macOS, use the Apple commands to inspect your Xcode, simulator runtimes, and devices:

```bash
maui apple xcode list
maui apple runtime list
maui apple simulator list
```

For DevFlow app automation, initialize the bundled agent skills in your app workspace, then add the in-app agent package as described in [DevFlow](src/DevFlow/README.md):

```bash
maui devflow init
```

## Manual setup details

If you prefer to install pieces yourself, install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), install the MAUI workload with `dotnet workload install maui`, and configure each platform's native tools directly (Xcode on macOS; Android Studio, Android SDK, and JDK for Android).

The CLI can still manage individual pieces when you do not want the full interactive setup: `maui android jdk install`, `maui android sdk install`, `maui android emulator create`, and the `maui apple ...` commands on macOS.

## Products

### Cli

A command-line tool for .NET MAUI development environment setup, device management, and app automation.

- **Environment diagnostics** (`maui doctor`) with auto-fix capabilities
- **Android SDK and JDK management** (`maui android`) — install, update, and configure
- **Emulator management** (`maui android emulator`) — create, start, stop, and delete Android emulators
- **Apple platform management** (`maui apple`) — Xcode, simulator, and runtime management (macOS)
- **Device listing** (`maui device list`) across all connected platforms
- **DevFlow app automation** (`maui devflow`) — visual tree inspection, element interaction, screenshots, WebView/CDP automation, network monitoring, profiling, storage access, real-time log/sensor streaming, and MCP server for AI agents
- **MAUI Go** (`maui go`) — create, serve, and upgrade single-file Comet Go projects for rapid prototyping
- **Version info** (`maui version`)
- **Global options** — `--json` for CI pipelines, `--verbose`, `--dry-run`, `--ci`

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Cli` | CLI global tool (`maui`) |

Start with the [fastest install path](#fastest-install-path), then run `maui <command> --help` for command-specific options.

### Comet

Experimental MVU UI framework for .NET MAUI — C# fluent UI, signals/reactive state, single-file apps via Comet Go.

| Package | Description |
|---------|-------------|
| `Comet` | Core MVU framework |
| `Comet.SourceGenerator` | Roslyn source generators for Comet |
| `Comet.Layout.Yoga` | Yoga layout integration |

### Go

Single-file Comet apps server + companion app for rapid prototyping (alpha; sister to Comet).

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.Go.Server` | Comet Go server for hosting single-file apps |

### DevFlow

A comprehensive MAUI testing, automation, and debugging toolkit. The DevFlow CLI is integrated into the `maui` CLI as `maui devflow` — see [Cli](#cli) above.

- **In-app HTTP agent** for visual tree inspection, element interaction, and screenshots
- **Blazor CDP bridge** for Chrome DevTools Protocol on Blazor WebViews
- **MCP server** for AI agent integration (via `maui devflow mcp`)
- **Platform drivers** for iOS, Android, Mac Catalyst, Windows, and Linux/GTK
- **Network monitoring** and **performance profiling**
- **Real-time streaming** — WebSocket channels for logs, network requests, sensor data, profiler samples, and UI events
- **Storage access** — read/write app preferences and secure storage
- **Device introspection** — battery, connectivity, geolocation, display info, and permissions

| Package | Description |
|---------|-------------|
| `Microsoft.Maui.DevFlow.Agent` | In-app agent for MAUI automation |
| `Microsoft.Maui.DevFlow.Agent.Core` | Platform-agnostic agent core |
| `Microsoft.Maui.DevFlow.Agent.Gtk` | GTK/Linux agent |
| `Microsoft.Maui.DevFlow.Blazor` | Blazor WebView CDP bridge |
| `Microsoft.Maui.DevFlow.Blazor.Gtk` | WebKitGTK CDP bridge |
| `Microsoft.Maui.DevFlow.Driver` | Platform driver library |
| `Microsoft.Maui.DevFlow.Logging` | Buffered JSONL file logger |

## Getting Started

See [CONTRIBUTING.md](CONTRIBUTING.md) for build instructions and development setup.

For the formal DevFlow HTTP and WebSocket contract, see [`docs/DevFlow/spec`](docs/DevFlow/spec/README.md).

## Agent Skills

This repository is also a marketplace for distributable agent skills for .NET MAUI development. Skills are organized as plugins compatible with Copilot CLI, Claude Code, and VS Code.

| Plugin | Description |
|--------|-------------|
| [`dotnet-maui`](plugins/dotnet-maui/) | MAUI development: DevFlow automation, profiling, accessibility, platform bindings, diagnostics |

```bash
# Install via Copilot CLI
/plugin marketplace add dotnet/maui-labs
/plugin install dotnet-maui@dotnet-maui-labs
```

See [plugins/](plugins/) for the full catalog and [plugins/CONTRIBUTING.md](plugins/CONTRIBUTING.md) for how to add skills.

## Support

See [SUPPORT.md](.github/SUPPORT.md) for how to file issues, get help, and the support policy for this repository.
