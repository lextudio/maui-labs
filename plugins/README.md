# Agent Skills

Distributable agent skills for .NET MAUI development. Installable via the Copilot CLI, Claude Code, or VS Code plugin system.

## Plugin

| Plugin | Skills | Description |
|--------|--------|-------------|
| [dotnet-maui](dotnet-maui/) | [devflow-connect](dotnet-maui/skills/devflow-connect/), [android-slim-bindings](dotnet-maui/skills/android-slim-bindings/), [ios-slim-bindings](dotnet-maui/skills/ios-slim-bindings/), [dotnet-workload-info](dotnet-maui/skills/dotnet-workload-info/) | MAUI development — DevFlow automation, profiling, native bindings, workload discovery, diagnostics. Some skills require the `maui` CLI tool. |

## Installation

```bash
# Add this repo as a marketplace
/plugin marketplace add dotnet/maui-labs

# Install the plugin
/plugin install dotnet-maui@dotnet-maui-labs
```

## Adding Skills

See [CONTRIBUTING.md](CONTRIBUTING.md) for the full guide. Quick summary:

1. Create `plugins/<plugin>/skills/<skill-name>/SKILL.md` with YAML frontmatter
2. Create `tests/<plugin>/<skill-name>/eval.yaml` with evaluation scenarios
3. Submit a PR — the `skill-check` workflow validates automatically
4. A maintainer posts `/evaluate` to run LLM-based evaluation
