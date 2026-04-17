// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Output;
using Spectre.Console;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AiCommands
{
	/// <summary>
	/// Creates the <c>maui ai init</c> command that bootstraps agent skill installation.
	/// </summary>
	static Command CreateInitCommand()
	{
		var skillOption = new Option<string[]>("--skill")
		{
			Description = "Install only specific skills (repeatable)",
			AllowMultipleArgumentsPerToken = true
		};

		var envOption = new Option<string[]>("--env")
		{
			Description = "Target only specific environments (repeatable, e.g. Claude, VsCode)",
			AllowMultipleArgumentsPerToken = true
		};

		var command = new Command("init", "Initialize AI agent skills for MAUI development")
		{
			CreateRepoOption(),
			CreateBranchOption(),
			CreateForceOption(),
			new Option<bool>("--no-mcp") { Description = "Skip MCP server configuration" },
			skillOption,
			envOption
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var isCi = parseResult.GetValue(GlobalOptions.CiOption);
			var dryRun = parseResult.GetValue(GlobalOptions.DryRunOption);
			var repo = parseResult.GetOption<string>("repo") ?? DefaultRepo;
			var branch = parseResult.GetOption<string>("branch") ?? DefaultBranch;
			var force = parseResult.GetOption<bool>("force");
			var noMcp = parseResult.GetOption<bool>("no-mcp");
			var skillFilter = parseResult.GetOption<string[]>("skill");
			var envFilter = parseResult.GetOption<string[]>("env");

			try
			{
				using var http = CreateGitHubHttpClient();

				// Step 1: Fetch marketplace and discover skills
				List<SkillInfo> allSkills;
				if (!useJson && formatter is SpectreOutputFormatter spectre)
				{
					allSkills = await spectre.StatusAsync("Fetching marketplace...", async () =>
						await FetchAllSkillsAsync(http, repo, branch));
				}
				else
				{
					allSkills = await FetchAllSkillsAsync(http, repo, branch);
				}

				if (allSkills.Count == 0)
				{
					formatter.WriteWarning("No skills found in the marketplace.");
					return 1;
				}

				// Step 2: Detect agent environments
				var workingDir = Directory.GetCurrentDirectory();
				var environments = AgentEnvironmentDetector.Detect(workingDir);

				// Filter environments if --env specified
				if (envFilter is { Length: > 0 })
				{
					environments = environments
						.Where(e => envFilter.Any(f =>
							string.Equals(f, e.Kind.ToString(), StringComparison.OrdinalIgnoreCase)))
						.ToList();
				}

				if (environments.Count == 0)
				{
					if (useJson)
					{
						formatter.WriteWarning("No agent environments detected.");
						return 1;
					}

					if (isCi)
					{
						// In CI mode, create .claude/ by default
						var claudeDir = Path.Combine(workingDir, ".claude");
						Directory.CreateDirectory(claudeDir);
						environments = AgentEnvironmentDetector.Detect(workingDir);
					}
					else
					{
						// Prompt user to create a default environment
						formatter.WriteWarning("No agent environments detected.");
						var create = AnsiConsole.Confirm(
							"Create [cyan].claude/[/] directory for Claude Code?", defaultValue: true);
						if (!create)
						{
							formatter.WriteInfo("No environments to configure. Exiting.");
							return 0;
						}

						var claudeDir = Path.Combine(workingDir, ".claude");
						Directory.CreateDirectory(claudeDir);
						environments = AgentEnvironmentDetector.Detect(workingDir);
					}
				}

				formatter.WriteInfo($"Detected {environments.Count} environment(s): {string.Join(", ", environments.Select(e => e.Kind))}");

				// Step 3: Select skills
				List<SkillInfo> selectedSkills;
				if (skillFilter is { Length: > 0 })
				{
					selectedSkills = allSkills
						.Where(s => skillFilter.Any(f =>
							string.Equals(f, s.Name, StringComparison.OrdinalIgnoreCase)))
						.ToList();

					if (selectedSkills.Count == 0)
					{
						formatter.WriteError(new Exception($"No skills matched filter: {string.Join(", ", skillFilter)}"));
						return 1;
					}
				}
				else if (useJson || isCi)
				{
					selectedSkills = allSkills;
				}
				else
				{
					var prompt = new MultiSelectionPrompt<string>()
						.Title("Select skills to install:")
						.PageSize(15)
						.InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to accept)[/]");

					foreach (var skill in allSkills)
					{
						var label = string.IsNullOrEmpty(skill.Description)
							? skill.Name
							: $"{skill.Name} - {skill.Description}";
						prompt.AddChoice(label);
					}

					// Pre-select all by default
					prompt.AddChoices(Array.Empty<string>());
					foreach (var skill in allSkills)
					{
						var label = string.IsNullOrEmpty(skill.Description)
							? skill.Name
							: $"{skill.Name} - {skill.Description}";
						prompt.Select(label);
					}

					var selected = AnsiConsole.Prompt(prompt);
					selectedSkills = allSkills
						.Where(s =>
						{
							var label = string.IsNullOrEmpty(s.Description)
								? s.Name
								: $"{s.Name} - {s.Description}";
							return selected.Contains(label);
						})
						.ToList();

					if (selectedSkills.Count == 0)
					{
						formatter.WriteInfo("No skills selected.");
						return 0;
					}
				}

				// Step 4: Confirmation
				if (!force && !isCi && !useJson)
				{
					formatter.WriteInfo($"Will install {selectedSkills.Count} skill(s) to {environments.Count} environment(s):");
					formatter.WriteTable(
						selectedSkills,
						("Skill", s => s.Name),
						("Plugin", s => s.PluginName),
						("Files", s => s.Files.Count.ToString()));

					if (!AnsiConsole.Confirm("Proceed with installation?", defaultValue: true))
					{
						formatter.WriteInfo("Installation cancelled.");
						return 0;
					}
				}

				if (dryRun)
				{
					formatter.WriteInfo("[Dry run] Would install the following skills:");
					formatter.WriteTable(
						from s in selectedSkills
						from e in environments
						select new { s.Name, Env = e.Kind.ToString(), Path = Path.Combine(e.SkillsDirectory, s.Name) },
						("Skill", x => x.Name),
						("Environment", x => x.Env),
						("Path", x => x.Path));
					return 0;
				}

				// Step 5: Install skills
				var results = new List<(string Skill, string Env, int Files, string Path)>();

				foreach (var env in environments)
				{
					foreach (var skill in selectedSkills)
					{
						var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
							http, skill, env, workingDir, repo, branch, force);

						results.Add((skill.Name, env.Kind.ToString(), filesInstalled, installPath));

						if (filesInstalled > 0)
							formatter.WriteSuccess($"Installed {skill.Name} → {env.Kind} ({filesInstalled} files)");
						else
							formatter.WriteInfo($"Skipped {skill.Name} → {env.Kind} (already installed)");
					}
				}

				// Step 6: Configure MCP
				if (!noMcp)
				{
					foreach (var env in environments)
					{
						var ok = await McpConfigurator.ConfigureAsync(env, workingDir);
						if (ok)
							formatter.WriteSuccess($"MCP configured for {env.Kind}");
						else
							formatter.WriteWarning($"Could not configure MCP for {env.Kind}");
					}
				}

				// Step 7: Summary
				formatter.WriteTable(
					results,
					("Skill", r => r.Skill),
					("Environment", r => r.Env),
					("Files", r => r.Files > 0 ? r.Files.ToString() : "skipped"),
					("Path", r => r.Path));

				if (useJson)
				{
					var jsonResult = new JsonObject
					{
						["status"] = "success",
						["skills"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
						{
							["skill"] = r.Skill,
							["environment"] = r.Env,
							["files"] = r.Files,
							["path"] = r.Path
						}).ToArray())
					};
					formatter.Write(jsonResult);
				}

				return 0;
			}
			catch (HttpRequestException ex)
			{
				formatter.WriteError(new Exception($"Network error: {ex.Message}. Check your connection or set GITHUB_TOKEN for higher rate limits."));
				return 1;
			}
			catch (Exception ex)
			{
				return Program.HandleCommandException(formatter, ex);
			}
		});

		return command;
	}

	/// <summary>
	/// Fetches all skills from every plugin listed in the marketplace manifest.
	/// </summary>
	static async Task<List<SkillInfo>> FetchAllSkillsAsync(HttpClient http, string repo, string branch)
	{
		var marketplace = await MarketplaceClient.GetMarketplaceAsync(http, repo, branch);
		if (marketplace is null)
			return [];

		var allSkills = new List<SkillInfo>();
		foreach (var pluginEntry in marketplace.Plugins)
		{
			var plugin = await MarketplaceClient.GetPluginAsync(http, repo, branch, pluginEntry.Source);
			if (plugin is null)
				continue;

			var skills = await MarketplaceClient.GetSkillsAsync(http, repo, branch, plugin, pluginEntry.Source);
			allSkills.AddRange(skills);
		}

		return allSkills;
	}
}
