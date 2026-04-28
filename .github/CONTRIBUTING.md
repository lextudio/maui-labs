# Contributing to .NET MAUI Labs

Thank you for your interest in contributing! This repository hosts experimental .NET MAUI packages that are in active development.

## Repository Structure

```
maui-labs/
├── src/                    # Source code, organized by product
│   └── {Product}/          # Each product has its own folder
│       ├── Version.props   # Per-product version
│       ├── {Product}.slnf  # Solution filter for this product
│       └── ...projects...
├── samples/                # Sample apps (not shipped)
├── playground/             # Manual verification/scratch apps
├── docs/                   # Documentation per product
├── eng/                    # Shared build infrastructure
└── MauiLabs.sln            # Full solution
```

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (see `global.json` for exact version)
- MAUI workload: `dotnet workload install maui`

### Building

```bash
# Build everything
dotnet build MauiLabs.sln

# Build just one product (e.g., DevFlow)
dotnet build src/DevFlow/DevFlow.slnf

# Build a specific project
dotnet build src/DevFlow/Microsoft.Maui.DevFlow.Agent.Core/
```

### Running Tests

```bash
# All tests
dotnet test MauiLabs.sln

# Just DevFlow tests
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```

### Opening in an IDE

For focused development on a single product, open the solution filter:

- **DevFlow**: `src/DevFlow/DevFlow.slnf`

For the full repo, open `MauiLabs.sln`.

## Adding a New Product

### 1. Source code

1. Create `src/{NewProduct}/` with:
   - `Version.props` (copy from an existing product)
   - Project folders with `.csproj` files
   - Test project
   - `{NewProduct}.slnf` solution filter
2. Add projects to `MauiLabs.sln`
3. Add any new package versions to `Directory.Packages.props`

### 2. GitHub Actions CI workflow

Create `.github/workflows/ci-{newproduct}.yml` that calls the reusable `_build.yml` workflow. Each product has its **own** CI workflow file with path filters scoped to its source.

> **Important**: Always include `types: [opened, synchronize, reopened, edited]` on the `pull_request` trigger. The `edited` type is required so CI re-runs when GitHub auto-retargets a PR after a stacked branch merges.

```yaml
name: CI - {NewProduct}

on:
  push:
    branches: [main]
    paths:
      - 'src/{NewProduct}/**'
      - 'eng/**'
      - 'Directory.Build.props'
      - 'Directory.Build.targets'
      - 'Directory.Packages.props'
      - 'global.json'
      - 'NuGet.config'
  pull_request:
    types: [opened, synchronize, reopened, edited]
    branches: [main]
    paths:
      - 'src/{NewProduct}/**'
      - 'eng/**'
      - 'Directory.Build.props'
      - 'Directory.Build.targets'
      - 'Directory.Packages.props'
      - 'global.json'
      - 'NuGet.config'

jobs:
  build:
    uses: ./.github/workflows/_build.yml
    with:
      project-path: src/{NewProduct}/{NewProduct}.slnf
      project-name: {newproduct}
      run-tests: true
      pack: true
```

See `_build.yml` for available inputs (`install-workloads`, `os`, `native-deps`, etc.).

### 3. Azure DevOps official pipeline

The official build/sign/publish pipeline lives in `eng/pipelines/devflow-official.yml`. For every new product you must add:

1. **A parameter** to gate NuGet.org publishing:
   ```yaml
   - name: publishNewProductNuget
     displayName: 'Publish NewProduct packages to NuGet.org'
     type: boolean
     default: false
   ```

2. **A build job** in the `build` stage (parallel with the existing jobs):
   ```yaml
   - job: NewProduct
     displayName: NewProduct - Windows
     pool:
       name: NetCore1ESPool-Internal
       demands: ImageOverride -equals windows.vs2026preview.scout.amd64
     strategy:
       matrix:
         Release:
           _BuildConfig: Release
           _OfficialBuildArgs: /p:DotNetSignType=$(_SignType)
             /p:TeamName=$(_TeamName)
             /p:OfficialBuildId=$(BUILD.BUILDNUMBER)
     steps:
     - task: UseDotNet@2
       displayName: Install .NET SDK
       inputs:
         useGlobalJson: true
     # Add workload/dependency steps if needed (see DevFlow job for example)
     - script: eng\common\cibuild.cmd
         -configuration $(_BuildConfig)
         -prepareMachine
         -projects $(Build.SourcesDirectory)\src\NewProduct\NewProduct.slnf
         $(_OfficialBuildArgs)
       displayName: Build and Test NewProduct
   ```

3. **A publish stage** (copy an existing `publish_*_nuget` stage block and update the package glob pattern to match your product's `.nupkg` names).

## Versioning

Each product manages its own version in `src/{Product}/Version.props`:

```xml
<Project>
  <PropertyGroup>
    <VersionPrefix>1.0.0</VersionPrefix>
    <VersionSuffix>preview.1</VersionSuffix>
  </PropertyGroup>
</Project>
```

## Package Management

This repo uses [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management). All package versions are defined in `Directory.Packages.props` at the repo root. Individual `.csproj` files reference packages without specifying versions.

## Code Style

- `ImplicitUsings` and `Nullable` are enabled repo-wide
- Follow standard .NET naming conventions

## Pull Requests

- PRs trigger CI builds only for products with changed files
- Ensure tests pass before requesting review
- Update `Version.props` if your change warrants a version bump
