// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;

namespace Microsoft.Maui.Cli.Commands;

/// <summary>
/// Root command group for AI-assisted MAUI development: install and manage agent skills.
/// </summary>
public static partial class AiCommands
{
	private const string DefaultRepo = "dotnet/maui-labs";
	private const string DefaultBranch = "main";
	private const string DefaultMarketplacePath = ".github/plugin/marketplace.json";

	public static Command Create()
	{
		var command = new Command("ai", "AI-assisted MAUI development: install and manage agent skills");
		command.Add(CreateInitCommand());
		command.Add(CreateListCommand());
		command.Add(CreateStatusCommand());
		command.Add(CreateUpdateCommand());
		command.Add(CreateAddCommand());
		return command;
	}

	/// <summary>
	/// Creates the shared --repo option used by multiple subcommands.
	/// </summary>
	static Option<string> CreateRepoOption() =>
		new("--repo") { Description = "GitHub repository", DefaultValueFactory = _ => DefaultRepo };

	/// <summary>
	/// Creates the shared --branch / -b option used by multiple subcommands.
	/// </summary>
	static Option<string> CreateBranchOption() =>
		new("--branch", "-b") { Description = "GitHub branch", DefaultValueFactory = _ => DefaultBranch };

	/// <summary>
	/// Creates the shared --force / -y option for skipping confirmation prompts.
	/// </summary>
	static Option<bool> CreateForceOption() =>
		new("--force", "-y") { Description = "Skip confirmation prompts" };

	/// <summary>
	/// Creates an <see cref="HttpClient"/> configured for GitHub API access.
	/// Respects the <c>GITHUB_TOKEN</c> environment variable for authentication.
	/// </summary>
	static HttpClient CreateGitHubHttpClient()
	{
		var http = new HttpClient();
		http.DefaultRequestHeaders.UserAgent.Add(
			new System.Net.Http.Headers.ProductInfoHeaderValue("Microsoft.Maui.Cli", "1.0"));
		http.DefaultRequestHeaders.Accept.Add(
			new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

		var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
		if (!string.IsNullOrEmpty(token))
			http.DefaultRequestHeaders.Authorization =
				new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

		return http;
	}
}
