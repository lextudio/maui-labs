# Agent Skills

Distributable agent skills for .NET MAUI development. Each plugin is independently installable via the Copilot CLI, Claude Code, or VS Code plugin system.

## Plugins

| Plugin | Skills | Description |
|--------|--------|-------------|
| [dotnet-maui-devflow](dotnet-maui-devflow/) | [devflow-connect](dotnet-maui-devflow/skills/devflow-connect/) | DevFlow automation — agent connectivity, visual tree inspection, screenshots, app interactions. Requires the `maui` CLI and DevFlow agent packages. |
| [dotnet-maui-dev](dotnet-maui-dev/) | _(accepting contributions)_ | General MAUI development — profiling, accessibility, UI design patterns, platform guidance. |

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
