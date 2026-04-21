# MIBC / ReadyToRun Optimization Guide

Use this guide when the startup trace shows significant time in .NET Runtime (JIT compilation, assembly loading) or when overall startup needs to be faster. MIBC-based Partial R2R pre-compiles the methods your app actually uses at startup, reducing JIT time while keeping the app smaller than full R2R.

## Concepts

| Term | Meaning |
|---|---|
| **R2R (ReadyToRun)** | Ahead-of-time compiled native code embedded in .NET assemblies. Eliminates JIT at startup. |
| **Full R2R** | Every method is pre-compiled. Fast startup but large app size. |
| **Partial R2R** | Only methods listed in a PGO profile are pre-compiled. Smaller than full R2R. |
| **MIBC** | Managed Instrumented Block Count — a profile format listing methods observed during a trace. Used by `crossgen2` to decide which methods to pre-compile in Partial R2R. |
| **PGO** | Profile-Guided Optimization — using runtime data to guide compilation decisions. |

## Platform Support Matrix

- In .NET 11+, CoreCLR is the default runtime on **all platforms** (Android, iOS, Mac Catalyst, Windows)
- MIBC/R2R profiles work on any CoreCLR target — collect per-platform profiles for best results
- For Native AOT apps, `@(_ReadyToRunPgoFiles)` does not have any effect

## Workflow: Generate and Apply a Custom MIBC Profile

### Step 1: Collect with `--format mibc`

```sh
maui profile startup \
  --project MyApp.csproj \
  -f net10.0-android \
  --format mibc \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

This produces both a `.mibc` file and a raw `.nettrace` companion. The MIBC file is ready to use — no manual conversion needed. The `.nettrace` is kept for analysis (see SKILL.md Step 2).

> **Alternative:** If `--format mibc` is not available (requires the latest `maui` CLI), collect with `--format nettrace` and convert manually:
> ```sh
> dotnet-pgo create-mibc --trace <name>.nettrace --output <name>.mibc
> ```
> Install `dotnet-pgo` if needed: `dotnet tool install -g dotnet-pgo`

### Step 2: Add the MIBC profile to your project

Create a directory for profiles and copy the `.mibc` from Step 1:

```sh
mkdir -p pgo/android
cp <name>.mibc pgo/android/startup.mibc
```

In your `.csproj`, add the MIBC file as a `_ReadyToRunPgoFiles` item:

```xml
<ItemGroup Condition="$(TargetFramework.Contains('-android'))">
  <_ReadyToRunPgoFiles Include="pgo/android/*.mibc" />
</ItemGroup>
<ItemGroup Condition="$(TargetFramework.Contains('-ios'))">
  <_ReadyToRunPgoFiles Include="pgo/ios/*.mibc" />
</ItemGroup>
```

### Step 3: Build with Partial R2R

.NET MAUI apps on CoreCLR use **Composite Partial ReadyToRun** by default in Release builds. No additional MSBuild properties are needed — the `_ReadyToRunPgoFiles` items are automatically consumed by `crossgen2`.

To verify Partial R2R is active, check that these defaults are in effect:
```xml
<!-- These are set by the MAUI SDK — do NOT set them manually unless overriding -->
<!-- <PublishReadyToRun>true</PublishReadyToRun> -->
<!-- <PublishReadyToRunComposite>true</PublishReadyToRunComposite> -->
<!-- <RunAOTCompilation>false</RunAOTCompilation> -->
```

If you previously set `<RunAOTCompilation>true</RunAOTCompilation>` to force full R2R, consider removing it and using custom MIBC profiles instead for a better size/speed tradeoff.

### Step 4: Re-measure

After applying the MIBC profile, re-run the profiling to verify improvement:

```sh
maui profile startup \
  --project MyApp.csproj \
  -f net10.0-android \
  --format mibc \
  --stopping-event-provider-name Microsoft.Maui.StartupProfiling \
  --stopping-event-event-name StartupComplete
```

Compare the `.NET Runtime` category percentage before and after. With a good startup MIBC profile, expect:
- Significant reduction in JIT-related samples in the .NET Runtime category
- Overall faster startup (the magnitude depends on how JIT-heavy your app's startup was)

## Troubleshooting

### `dotnet-pgo create-mibc` produces empty or minimal MIBC
- This applies only when manually converting (if `--format mibc` is not available).
- Ensure the trace was collected with `--format nettrace` (not speedscope-only).
- The `maui profile startup` command automatically includes the `Microsoft-Windows-DotNETRuntime` provider with JIT/R2R events.

### App still slow after MIBC
- Check that the MIBC file is for the correct platform and architecture.
- Verify `_ReadyToRunPgoFiles` is being picked up: build with `/v:diag` and search for `ReadyToRunPgo` in the log.
- The bottleneck may not be JIT — re-analyze the trace to see if the cost shifted to another category.

### MIBC profiles and app updates
- MIBC profiles should be regenerated when:
  - Major dependency updates (new NuGet packages change startup method set)
  - Significant app architecture changes
  - .NET SDK version upgrades
- Minor code changes typically don't invalidate the profile — unrecognized methods are simply JIT-compiled as usual.

## References

- [Runtimes and compilation in .NET MAUI](https://learn.microsoft.com/dotnet/maui/deployment/runtimes-compilation)
- [dotnet/maui PR #33234 — Enable Composite Partial ReadyToRun on Android](https://github.com/dotnet/maui/pull/33234)
- [dotnet-pgo documentation](https://learn.microsoft.com/dotnet/core/tools/dotnet-pgo)
