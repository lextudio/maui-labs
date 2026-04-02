# Agent Skills

Distributable agent skills for .NET MAUI development. Each plugin is independently installable via the Copilot CLI, Claude Code, or VS Code plugin system.

## Plugins

| Plugin | Skills | Description |
|--------|--------|-------------|
| [dotnet-maui-devflow](dotnet-maui-devflow/) | [devflow-connect](dotnet-maui-devflow/skills/devflow-connect/) | DevFlow automation — agent connectivity, visual tree inspection, screenshots, app interactions. Requires the `maui` CLI and DevFlow agent packages. |
| [dotnet-maui-dev](dotnet-maui-dev/) | [android-slim-bindings](dotnet-maui-dev/skills/android-slim-bindings/), [ios-slim-bindings](dotnet-maui-dev/skills/ios-slim-bindings/), [dotnet-workload-info](dotnet-maui-dev/skills/dotnet-workload-info/), [maui-release-notes](dotnet-maui-dev/skills/maui-release-notes/) | General MAUI development — native bindings, workload discovery, release notes, and more. |

## Installation

```bash
# Add this repo as a marketplace
/plugin marketplace add dotnet/maui-labs

# Install a plugin
/plugin install dotnet-maui-devflow@dotnet-maui-labs-skills
/plugin install dotnet-maui-dev@dotnet-maui-labs-skills
```

## Adding Skills

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide. Quick summary:

1. Create `plugins/<plugin>/skills/<skill-name>/SKILL.md` with YAML frontmatter
2. Create `tests/<plugin>/<skill-name>/eval.yaml` with evaluation scenarios
3. Submit a PR — the `skill-check` workflow validates automatically
4. A maintainer posts `/evaluate` to run LLM-based evaluation
