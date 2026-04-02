# Repository Instructions

This repository contains experimental .NET MAUI packages and distributable agent skills under `plugins/`.

## Skills

Skills are organized as plugins under `plugins/`. Each subdirectory in `plugins/` is an independent plugin (e.g., `plugins/dotnet-maui-labs`).

### Plugin Structure

```
plugins/<plugin-name>/
├── plugin.json              # Plugin manifest (name, version, description, skills path)
└── skills/
    └── <skill-name>/
        ├── SKILL.md         # Skill definition (required)
        └── references/      # Supporting documentation (optional)
```

### Skill Format

Each `SKILL.md` must have YAML frontmatter:

```yaml
---
name: skill-name
description: >-
  What this skill does. USE FOR: specific scenarios.
  DO NOT USE FOR: non-applicable contexts.
---
```

The `description` field is critical — agent runtimes read only the description to decide whether to activate the skill. Include explicit "USE FOR" and "DO NOT USE FOR" guidance.

### Adding a New Skill

1. Create `plugins/<plugin>/skills/<skill-name>/SKILL.md` with frontmatter + content
2. Create `tests/<plugin>/<skill-name>/eval.yaml` with evaluation scenarios
3. Run validation: download `skill-validator` from [dotnet/skills releases](https://github.com/dotnet/skills/releases/tag/skill-validator-nightly), then:
   ```bash
   skill-validator check --plugin plugins/<plugin>
   ```
4. Submit a PR — the `skill-check` workflow validates structure automatically

### Evaluation

Skills are evaluated using LLM-based pairwise comparison (with vs. without the skill). Evaluation runs are triggered by posting `/evaluate` on a PR. See `tests/` for `eval.yaml` examples.

## Build

### Cli and DevFlow packages

```bash
dotnet build src/Cli/Microsoft.Maui.Cli.sln
dotnet build src/DevFlow/Microsoft.Maui.DevFlow.sln
```

### Running tests

```bash
dotnet test src/Cli/Microsoft.Maui.Cli.UnitTests/
dotnet test src/DevFlow/Microsoft.Maui.DevFlow.Tests/
```
