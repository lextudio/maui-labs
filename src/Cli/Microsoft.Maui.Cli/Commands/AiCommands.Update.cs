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
	/// Creates the <c>maui ai update</c> command that updates installed skills to the latest version.
	/// </summary>
	static Command CreateUpdateCommand()
	{
		var skillOption = new Option<string[]>("--skill")
		{
			Description = "Update only specific skills (repeatable)",
			AllowMultipleArgumentsPerToken = true
		};

		var command = new Command("update", "Update installed AI agent skills to the latest version")
		{
			CreateRepoOption(),
			CreateBranchOption(),
			CreateForceOption(),
			skillOption
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
			var skillFilter = parseResult.GetOption<string[]>("skill");

			try
			{
				var workingDir = Directory.GetCurrentDirectory();
				var environments = AgentEnvironmentDetector.Detect(workingDir);

				if (environments.Count == 0)
				{
					formatter.WriteWarning("No agent environments detected. Run 'maui ai init' first.");
					return 1;
				}

				using var http = CreateGitHubHttpClient();

				// Scan installed skills and check for updates
				var updatable = new List<(DetectedEnvironment Env, string SkillDir, string SkillName, InstalledSkillVersion Version)>();

				foreach (var env in environments)
				{
					if (!Directory.Exists(env.SkillsDirectory))
						continue;

					foreach (var skillDir in Directory.GetDirectories(env.SkillsDirectory))
					{
						var skillName = Path.GetFileName(skillDir);

						if (skillFilter is { Length: > 0 } &&
							!skillFilter.Any(f => string.Equals(f, skillName, StringComparison.OrdinalIgnoreCase)))
							continue;

						var version = await SkillVersionStore.ReadAsync(skillDir);
						if (version is null)
							continue;

						// Check if update is available
						if (version.PluginPath is not null)
						{
							var remoteSha = await MarketplaceClient.GetRemoteCommitShaAsync(
								http, repo, branch, version.PluginPath);

							var needsUpdate = force ||
								remoteSha is null ||
								version.Commit is null ||
								!string.Equals(remoteSha, version.Commit, StringComparison.OrdinalIgnoreCase);

							if (needsUpdate)
								updatable.Add((env, skillDir, skillName, version));
						}
					}
				}

				if (updatable.Count == 0)
				{
					formatter.WriteSuccess("All skills are up to date.");
					return 0;
				}

				formatter.WriteInfo($"Found {updatable.Count} skill(s) with updates available.");

				if (dryRun)
				{
					formatter.WriteInfo("[Dry run] Would update the following skills:");
					formatter.WriteTable(
						updatable,
						("Skill", u => u.SkillName),
						("Environment", u => u.Env.Kind.ToString()),
						("Current Commit", u => (u.Version.Commit ?? "unknown")[..Math.Min(u.Version.Commit?.Length ?? 7, 7)]),
						("Path", u => u.SkillDir));
					return 0;
				}

				// Confirm unless --force, --ci, or --json
				if (!force && !isCi && !useJson)
				{
					formatter.WriteTable(
						updatable,
						("Skill", u => u.SkillName),
						("Environment", u => u.Env.Kind.ToString()),
						("Path", u => u.SkillDir));

					if (!AnsiConsole.Confirm("Proceed with update?", defaultValue: true))
					{
						formatter.WriteInfo("Update cancelled.");
						return 0;
					}
				}

				// Fetch marketplace to get skill metadata for re-download
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

				var results = new List<(string Skill, string Env, int Files)>();

				foreach (var (env, skillDir, skillName, _) in updatable)
				{
					var skillInfo = allSkills.FirstOrDefault(s =>
						string.Equals(s.Name, skillName, StringComparison.OrdinalIgnoreCase));

					if (skillInfo is null)
					{
						formatter.WriteWarning($"Skill '{skillName}' not found in marketplace, skipping.");
						continue;
					}

					var (filesInstalled, _) = await SkillInstaller.InstallSkillAsync(
						http, skillInfo, env, workingDir, repo, branch, force: true);

					results.Add((skillName, env.Kind.ToString(), filesInstalled));
					formatter.WriteSuccess($"Updated {skillName} → {env.Kind} ({filesInstalled} files)");
				}

				if (useJson)
				{
					var jsonResult = new JsonObject
					{
						["status"] = "success",
						["updated"] = new JsonArray(results.Select(r => (JsonNode)new JsonObject
						{
							["skill"] = r.Skill,
							["environment"] = r.Env,
							["files"] = r.Files
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
}
