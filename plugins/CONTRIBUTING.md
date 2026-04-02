# Contributing Skills

This guide covers how to add agent skills to the maui-labs marketplace.

## Skill Structure

```
plugins/dotnet-maui-devflow/skills/<skill-name>/
├── SKILL.md           # Skill definition (required)
├── references/        # Detailed reference docs (optional)
│   └── *.md
└── scripts/           # Helper scripts (optional)
    └── *.ps1 / *.sh
```

## SKILL.md Format

Every skill must have YAML frontmatter:

```yaml
---
name: my-skill-name
description: >-
  Short description of what this skill does.
  USE FOR: the specific scenarios where this skill applies.
  DO NOT USE FOR: scenarios where this skill should NOT be activated.
---
```

### Frontmatter Rules

- **`name`** — kebab-case, descriptive (e.g., `devflow-visual-tree`, `maui-build-diagnosis`)
- **`description`** — This is the only thing agent runtimes read first to decide activation. Be explicit about scope.

### Body Guidelines

- Keep under 500 lines; split detailed content into `references/` files
- Include: Purpose, When to Use, Inputs, Workflow (numbered steps), Validation
- Use concrete checklists and CLI commands, not vague guidance
- Reference the `maui` CLI and DevFlow APIs where applicable

## Evaluation Tests

Every skill should have evaluation scenarios:

```
tests/<plugin-name>/<skill-name>/
└── eval.yaml
```

### eval.yaml Format

```yaml
scenarios:
  - name: "Descriptive scenario name"
    prompt: |
      The prompt sent to the agent. Be specific about the user's
      situation, platform, error messages, etc.
    assertions:
      - type: "output_contains"
        value: "expected text in response"
      - type: "output_matches"
        pattern: "regex.*pattern"
      - type: "output_not_contains"
        value: "text that should NOT appear"
    rubric:
      - "Agent recommends the correct diagnostic tool"
      - "Agent provides platform-appropriate commands"
      - "Agent does not suggest deprecated approaches"
    timeout: 120
```

### Assertion Types

| Type | Description |
|------|-------------|
| `output_contains` | Response includes exact text |
| `output_not_contains` | Response must NOT include text |
| `output_matches` | Response matches regex pattern |
| `output_not_matches` | Response must NOT match regex |

### Rubric Guidelines

Rubric items are judged by an LLM comparing responses with and without the skill. Good rubric items are:
- **Specific** — "Recommends `maui doctor` as first step" not "Gives good advice"
- **Measurable** — Can be objectively evaluated from the response
- **Relevant** — Tests what the skill actually teaches

## Validation

### Local Testing

Download the skill-validator from [dotnet/skills releases](https://github.com/dotnet/skills/releases/tag/skill-validator-nightly):

```bash
# Static validation (no LLM)
skill-validator check --plugin plugins/dotnet-maui-labs

# LLM evaluation (requires GitHub auth)
skill-validator evaluate \
  --runs 3 \
  --tests-dir tests/dotnet-maui-labs \
  plugins/dotnet-maui-labs/skills
```

### CI

- **skill-check** — Runs automatically on every PR that modifies `plugins/` or `tests/`
- **skill-evaluation** — Triggered by posting `/evaluate` on a PR (write access required)

## PR Checklist

- [ ] `SKILL.md` has valid YAML frontmatter with `name` and `description`
- [ ] Description includes "USE FOR" and "DO NOT USE FOR"
- [ ] `eval.yaml` has at least 3 scenarios with assertions and rubric
- [ ] `skill-validator check` passes locally
- [ ] Body under 500 lines (use `references/` for detailed content)
