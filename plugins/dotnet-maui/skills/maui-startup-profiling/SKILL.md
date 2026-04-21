---
name: maui-startup-profiling
description: >-
  Diagnose slow .NET MAUI app startup using the `maui profile startup` CLI tool.
  Collects startup traces with MIBC format by default (producing both .mibc and
  .nettrace), analyzes hotspots with `dotnet-trace report topN`, and optionally
  categorizes CPU time across platform/OS, .NET runtime, MAUI framework, bindings,
  and app code via speedscope conversion. Recommends targeted optimizations and
  generates MIBC profiles for ReadyToRun on Android.
  USE FOR: "why is my app slow to start?", "what makes my MAUI app startup slow?",
  startup performance analysis, generating MIBC/PGO profiles for R2R, reducing
  Android/iOS app launch time, profiling MAUI startup, analyzing startup traces.
  DO NOT USE FOR: runtime CPU profiling after startup, memory leak analysis,
  build performance issues, environment setup (use dotnet-maui-doctor), or
  general .NET profiling unrelated to MAUI apps.
---

# MAUI Startup Profiling

Diagnose and optimize .NET MAUI app startup time using trace-based analysis.

## Workflow Overview

1. **Collect** a startup trace with `maui profile startup --format mibc`
2. **Analyze** the trace with `dotnet-trace report topN` and/or `scripts/analyze_speedscope.cs`
3. **Diagnose** bottlenecks using performance tips (see `references/performance-tips.md`)
4. **Apply MIBC** profile to reduce JIT overhead on Android (see `references/mibc-r2r-guide.md`)
5. **Re-measure** to verify improvement

## Step 1: Collect a Startup Trace

### Prerequisites

- The `maui` CLI tool installed (`dotnet tool install -g Microsoft.Maui.Cli`)
- .NET 10+ SDK (the CLI uses `dnx` to auto-resolve `dotnet-trace` and `dotnet-dsrouter` — no manual install needed)
- A MAUI app project
- A connected device or running emulator

### Command

Collect with `--format mibc` to get all three outputs at once — the MIBC profile, the raw `.nettrace`, and the data needed for analysis:

```sh
maui profile startup \
  --project <path-to-csproj> \
  -f <target-framework> \
  --format mibc \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

This produces:
- `<name>.mibc` — ready-to-use PGO profile for ReadyToRun (apply directly in Step 4)
- `<name>.nettrace` — raw companion trace (use for analysis and can be converted to speedscope)

**Always use an explicit stop condition.** Without `--stopping-event-*` or `--duration`, the tool waits for manual Enter, which is unsuitable for automated analysis.

### Platform-specific notes

| Platform | Framework example | Auto-stop | Notes |
|---|---|---|---|
| Android | `net10.0-android` | ✅ Auto-injected | CLI injects `StartupProfilingMarker` at build time |
| iOS | `net10.0-ios` | ⚠️ Not yet wired | Build injection is not yet enabled for iOS in the CLI (the bootstrap code is platform-agnostic but the CLI skips it for iOS). App must reference `Microsoft.Maui.StartupProfiling` NuGet and call `StartupProfilingMarker.Complete()`, or use `--duration 00:00:15` instead. |

### iOS manual integration

If the iOS app does not reference `Microsoft.Maui.StartupProfiling`, either:

1. Add the package and call `Complete()`:
```xml
<PackageReference Include="Microsoft.Maui.StartupProfiling" />
```
```csharp
protected override void OnAppearing()
{
    base.OnAppearing();
    StartupProfilingMarker.Complete();
}
```

2. Or use a fixed duration:
```sh
maui profile startup --project MyApp.csproj -f net10.0-ios --duration 00:00:15
```

## Step 2: Analyze the Trace

### Quick analysis with `dotnet-trace report`

The fastest way to identify the highest-impact code paths — no parsing or scripts needed:

```sh
# Top 30 methods by exclusive (self) time
dotnet-trace report <name>.nettrace topN -n 30

# Top 30 methods by inclusive time (includes callees)
dotnet-trace report <name>.nettrace topN -n 30 --inclusive

# Show full method signatures (not truncated)
dotnet-trace report <name>.nettrace topN -n 30 -v
```

This directly shows which methods consumed the most CPU during startup. Look for:
- **JIT/runtime methods** (e.g., `coreclr!*`, `System.Reflection.*`) → Apply MIBC (Step 4)
- **DI methods** (e.g., `ServiceProvider.GetService`) → Simplify registrations
- **App constructors** → Defer work, use async patterns
- **XAML/handler methods** → Simplify first page, use compiled bindings

### Categorized analysis with speedscope

For a deeper breakdown by component category, convert the `.nettrace` to speedscope and run the analysis script:

```sh
# Convert .nettrace to speedscope format
dotnet-trace convert <name>.nettrace --format speedscope

# Run categorized analysis (C# file-based app — requires only .NET SDK)
dotnet run scripts/analyze_speedscope.cs -- <name>.speedscope.json \
  --app-namespaces MyApp.,MyLib. \
  --top 15
```

**Parameters:**
- `--app-namespaces` — Comma-separated namespace prefixes that identify the user's app code. Without this, app code may be miscategorized as "BCL / Third-party".
- `--top N` — Number of top methods per category (default: 15).
- `--json` — Machine-readable JSON output.

**Categories (checked in precedence order):**

| Priority | Category | What it contains |
|---|---|---|
| 1 | App Code | User-specified namespace prefixes |
| 2 | MAUI Framework | `Microsoft.Maui.*`, `Microsoft.Extensions.DependencyInjection.*` |
| 3 | .NET Bindings | `Java.Interop.*`, `Android.*` (managed), `ObjCRuntime.*`, `UIKit.*` (managed) |
| 4 | .NET Runtime | `System.*`, `coreclr!*`, JIT, GC, assembly loading |
| 5 | BCL / Third-party | Remaining managed frames (NuGet packages, BCL not in Runtime) |
| 6 | Platform / OS | `java.*`, `android.*`, `dalvik.*`, `objc_*`, native frames |
| 7 | Unknown | Unresolvable frames |

> **Important:** Both tools report **CPU sample distribution**, not wall-clock startup time. Use Android ActivityManager logs (`adb logcat | grep ActivityManager`) or Instruments for wall-clock timing.

## Step 3: Diagnose and Optimize

Based on the analysis:

- **High App Code %** → Read `references/performance-tips.md` §Lazy Init, §Async, §Page Complexity
- **High MAUI Framework %** → Read `references/performance-tips.md` §DI, §Shell, §XAML, §Compiled Bindings
- **High .NET Runtime %** → Apply MIBC profile (Step 4); also see §Trimming in `references/performance-tips.md`
- **High .NET Bindings %** → Look for excessive managed↔native transitions; batch platform calls
- **High Platform/OS %** → Usually not directly actionable from app code; verify no redundant native init

## Step 4: Apply MIBC Profile (CoreCLR)

The `--format mibc` collection in Step 1 already produced a `.mibc` file. Apply it to pre-compile startup-critical methods with ReadyToRun. Read `references/mibc-r2r-guide.md` for the full guide.

**Quick summary:**
```sh
# The .mibc file was already generated in Step 1. Copy it to your project:
mkdir -p pgo/
cp <name>.mibc pgo/startup-<platform>.mibc # android|ios|maccatalyst|windows
```

Add to `.csproj`:
```xml
<!-- this sample shows `android` but it would be the same for ios, maccatalyst, or windows -->
<ItemGroup Condition="$(TargetFramework.Contains('-android'))">
  <_ReadyToRunPgoFiles Include="pgo/startup-android.mibc" />
</ItemGroup>
```

Re-measure:
```sh
maui profile startup --project MyApp.csproj -f net10.0-android --format mibc \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

## CLI Reference

```
maui profile startup [options]

Options:
  --project <path>                  Path to .csproj or containing directory
  -f, --framework <tfm>            Target framework (e.g., net10.0-android)
  -d, --device <id>                Device/simulator identifier
  -o, --output <path>              Output trace path
  --format <nettrace|speedscope|mibc>  Output format (default: nettrace)
  -c, --configuration <cfg>        Build configuration (default: Release)
  --duration <hh:mm:ss>            Fixed trace duration
  --no-build                       Skip build, use existing outputs
  --stopping-event-provider-name   EventSource provider for auto-stop
  --stopping-event-event-name      Event name for auto-stop
  --trace-profile <profile>        dotnet-trace profile (e.g., dotnet-sampled-thread-time)
  --json                           Machine-readable JSON output
```

## Interpreting Results — Common Patterns

### Pattern: DI-heavy startup
```
MAUI Framework    45%   ← Microsoft.Extensions.DependencyInjection dominates
App Code          25%
.NET Runtime      20%
```
**Action:** Reduce DI registrations, use factory methods, defer non-critical services.

### Pattern: JIT-heavy startup
```
.NET Runtime      55%   ← JIT compilation dominates
MAUI Framework    20%
App Code          15%
```
**Action:** Generate MIBC profile and apply Partial R2R. See `references/mibc-r2r-guide.md`.

### Pattern: Complex first page
```
MAUI Framework    35%
App Code          30%   ← Page constructors and layout
.NET Bindings     20%   ← Many platform view creations
```
**Action:** Simplify first page, defer content below fold, use Shell for lazy loading.

### Pattern: Sync I/O on startup
```
App Code          50%   ← Database/network calls blocking UI thread
MAUI Framework    25%
```
**Action:** Move to async patterns, defer I/O to after first render.
