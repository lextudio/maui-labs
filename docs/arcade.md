# Arcade Build System

This repository uses the [dotnet/arcade](https://github.com/dotnet/arcade) build system for versioning, packaging, signing, publishing, and CI/CD infrastructure.

## Overview

Arcade provides:
- **Shared build tooling** via `eng/common/` scripts and templates
- **Consistent versioning** using SemVer2 with automatic build metadata
- **Dependency flow** through [darc](https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md) and [Maestro](https://github.com/dotnet/arcade/blob/main/Documentation/Maestro.md)
- **Publishing infrastructure** for NuGet packages via Azure DevOps pipelines
- **Azure Pipelines templates** for CI, official builds, signing, and validation

## Repository Structure

```
├── global.json                  # .NET SDK version + Arcade SDK reference
├── Directory.Build.props        # Arcade SDK props import + shared settings
├── Directory.Build.targets      # Arcade SDK targets import
├── Directory.Packages.props     # Central Package Management (versions from eng/Versions.props)
├── NuGet.config                 # Package sources (dnceng feeds + nuget.org)
└── eng/
    ├── Versions.props           # Version prefix, prerelease label, all dependency versions
    ├── Version.Details.xml      # Dependency tracking for darc/maestro
    ├── Publishing.props         # V3 publishing configuration
    ├── Common.props             # Repo-specific shared properties
    ├── Common.targets           # Repo-specific shared targets
    ├── Signing.props            # ESRP/MicroBuild signing configuration
    └── common/                  # Shared Arcade infrastructure (do not edit manually)
```

## Versioning

Versioning is controlled by `eng/Versions.props`:

| Property | Value | Description |
|----------|-------|-------------|
| `VersionPrefix` | `0.23.1` | SemVer MAJOR.MINOR.PATCH |
| `PreReleaseVersionLabel` | `preview` | Pre-release label |
| `PreReleaseVersionIteration` | `1` | Pre-release iteration number |

**Build version formats:**
- Local dev build: `0.23.1-dev`
- CI/PR build: `0.23.1-ci`
- Official daily build: `0.23.1-preview.1.YYMMDD.R`
- Release build: `0.23.1`

## Dependency Management

### Flowable Dependencies (managed by darc/maestro)

These dependencies are tracked in `eng/Version.Details.xml` and their versions are automatically updated via Maestro subscriptions:

| Package | Source Repository |
|---------|-------------------|
| Microsoft.Maui.Controls | [dotnet/maui](https://github.com/dotnet/maui) |
| Microsoft.AspNetCore.Components.WebView.Maui | [dotnet/maui](https://github.com/dotnet/maui) |
| Microsoft.Extensions.Http | [dotnet/runtime](https://github.com/dotnet/runtime) |
| Microsoft.Extensions.Hosting | [dotnet/runtime](https://github.com/dotnet/runtime) |
| Microsoft.Extensions.Logging.Abstractions | [dotnet/runtime](https://github.com/dotnet/runtime) |
| Microsoft.Extensions.Logging.Debug | [dotnet/runtime](https://github.com/dotnet/runtime) |

### Version Properties

All NuGet package versions are centralized in `eng/Versions.props` as MSBuild properties (e.g., `$(MicrosoftMauiControlsVersion)`). These properties are consumed by `Directory.Packages.props` for Central Package Management.

**To update a dependency version:**
1. Update the version property in `eng/Versions.props`
2. Update the `Version` attribute in `eng/Version.Details.xml` (for flowable deps)
3. The change flows to `Directory.Packages.props` automatically via property references

### Pinned Dependencies

Third-party packages not produced by dotnet repos are pinned in `eng/Versions.props` and not tracked by darc. These must be updated manually.

## Shipping vs Non-Shipping Packages

| Package | IsShipping | Published to NuGet.org |
|---------|------------|----------------------|
| Microsoft.Maui.DevFlow.Agent | ✅ true | Yes (on release) |
| Microsoft.Maui.DevFlow.Driver | ✅ true | Yes (on release) |
| Microsoft.Maui.DevFlow.CLI | ✅ true | Yes (on release) |
| All other packages | ❌ false | No (dev/internal feeds only) |

`IsShipping` is set to `false` by default in `Directory.Build.props`. Shipping projects override this in their `.csproj` files.

## Building

```bash
# Restore and build (uses Arcade build scripts)
./eng/common/cibuild.sh --configuration Release --prepareMachine

# Or standard dotnet CLI
dotnet build src/DevFlow/DevFlow.slnf -c Release

# Run tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests -c Release

# Pack NuGet packages
dotnet pack src/DevFlow/DevFlow.slnf -c Release
```

Build output goes to `artifacts/` (Arcade convention):
- `artifacts/bin/` — compiled binaries
- `artifacts/packages/` — NuGet packages
- `artifacts/log/` — build logs
- `artifacts/TestResults/` — test results

## eng/common/ Directory

The `eng/common/` folder is **copied wholesale from dotnet/arcade** and should not be edited manually. It is updated automatically via Maestro dependency flow when the Arcade SDK version is updated.

Key files:
- `build.sh` / `build.cmd` — Build entry points
- `cibuild.sh` / `cibuild.cmd` — CI build wrappers
- `templates/` — Azure Pipelines templates (public CI)
- `templates-official/` — Azure Pipelines templates (official/internal builds)

## Further Reading

- [Arcade SDK documentation](https://github.com/dotnet/arcade/blob/main/Documentation/ArcadeSdk.md)
- [Dependency flow](https://github.com/dotnet/arcade/blob/main/Documentation/BranchesChannelsAndSubscriptions.md)
- [Darc CLI reference](https://github.com/dotnet/arcade/blob/main/Documentation/Darc.md)
- [Publishing infrastructure](https://github.com/dotnet/arcade/blob/main/Documentation/CorePackages/Publishing.md)
- [Versioning scheme](https://github.com/dotnet/arcade/blob/main/Documentation/CorePackages/Versioning.md)
