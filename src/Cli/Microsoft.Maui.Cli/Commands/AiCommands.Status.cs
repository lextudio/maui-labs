// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Microsoft.Maui.Cli.Output;

namespace Microsoft.Maui.Cli.Commands;

public static partial class AiCommands
{
	/// <summary>
	/// Creates the <c>maui ai status</c> command that shows installed skill status and checks for updates.
	/// </summary>
	static Command CreateStatusCommand()
	{
		var command = new Command("status", "Show status of installed AI agent skills")
		{
			CreateRepoOption(),
			CreateBranchOption(),
			new Option<bool>("--check-updates") { Description = "Check remote repository for available updates" }
		};

		command.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
		{
			var formatter = Program.GetFormatter(parseResult);
			var useJson = parseResult.GetValue(GlobalOptions.JsonOption);
			var repo = parseResult.GetOption<string>("repo") ?? DefaultRepo;
			var branch = parseResult.GetOption<string>("branch") ?? DefaultBranch;
			var checkUpdates = parseResult.GetOption<bool>("check-updates");

			try
			{
				var workingDir = Directory.GetCurrentDirectory();
				var environments = AgentEnvironmentDetector.Detect(workingDir);

				if (environments.Count == 0)
				{
					formatter.WriteWarning("No agent environments detected. Run 'maui ai init' first.");
					return 1;
				}

				using var http = checkUpdates ? CreateGitHubHttpClient() : null;

				var rows = new List<(string Skill, string Env, string Installed, string Status)>();

				foreach (var env in environments)
				{
					if (!Directory.Exists(env.SkillsDirectory))
						continue;

					foreach (var skillDir in Directory.GetDirectories(env.SkillsDirectory))
					{
						var skillName = Path.GetFileName(skillDir);
						var version = await SkillVersionStore.ReadAsync(skillDir, ct);

						if (version is null)
						{
							rows.Add((skillName, env.Kind.ToString(), "Unknown", "Unknown"));
							continue;
						}

						var installed = version.UpdatedAt is not null
							? (DateTime.TryParse(version.UpdatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : version.UpdatedAt)
							: "Unknown";

						var status = "Installed";

						if (checkUpdates && http is not null && version.PluginPath is not null)
						{
							var remoteSha = await MarketplaceClient.GetRemoteCommitShaAsync(
								http, repo, branch, version.PluginPath, ct);

							if (remoteSha is not null && version.Commit is not null)
							{
								status = string.Equals(remoteSha, version.Commit, StringComparison.OrdinalIgnoreCase)
									? "Up to date"
									: "Update available";
							}
							else
							{
								status = "Up to date";
							}
						}

						rows.Add((skillName, env.Kind.ToString(), installed, status));
					}
				}

				if (rows.Count == 0)
				{
					formatter.WriteInfo("No skills installed. Run 'maui ai init' to get started.");
					return 0;
				}

				if (useJson)
				{
					var jsonArray = new JsonArray(rows.Select(r => (JsonNode)new JsonObject
					{
						["skill"] = r.Skill,
						["environment"] = r.Env,
						["installed"] = r.Installed,
						["status"] = r.Status
					}).ToArray());
					formatter.Write(jsonArray);
				}
				else
				{
					formatter.WriteTable(
						rows,
						("Skill", r => r.Skill),
						("Environment", r => r.Env),
						("Installed", r => r.Installed),
						("Status", r => r.Status));
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
