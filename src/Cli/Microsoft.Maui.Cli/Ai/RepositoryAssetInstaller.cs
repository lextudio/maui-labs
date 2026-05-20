// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Discovers and installs non-skill AI development assets from this repository.
/// </summary>
internal static class RepositoryAssetInstaller
{
	const string CopilotAgentsRoot = ".github/agents";
	const string CopilotAgentsDestinationRoot = ".github/agents";

	/// <summary>
	/// Discovers MAUI-related Copilot agent definitions from <c>.github/agents</c>.
	/// </summary>
	public static async Task<List<RepositoryAssetInfo>> GetCopilotAgentsAsync(
		HttpClient http,
		string repo,
		string branch,
		List<(string Path, string Type)>? cachedTreeEntries = null,
		CancellationToken ct = default)
	{
		var assets = new List<RepositoryAssetInfo>();
		var entries = cachedTreeEntries ?? await MarketplaceClient.FetchTreeEntriesAsync(http, repo, branch, ct).ConfigureAwait(false);
		if (entries is null)
			return assets;

		var prefix = MarketplaceClient.NormalizePath(CopilotAgentsRoot) + "/";
		foreach (var (entryPath, entryType) in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
		{
			if (entryType != "blob" ||
				!entryPath.StartsWith(prefix, StringComparison.Ordinal) ||
				!entryPath.EndsWith(".agent.md", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var content = await MarketplaceClient.FetchRawStringAsync(http, repo, branch, entryPath, ct).ConfigureAwait(false);
			if (content is null)
				continue;

			var (name, description) = MarketplaceClient.ParseFrontmatter(content);
			var assetName = name ?? GetRemoteFileName(entryPath)[..^".agent.md".Length];
			if (!IsMauiRelatedAgent(assetName, description, content))
				continue;

			assets.Add(new RepositoryAssetInfo
			{
				Name = assetName,
				Category = "agent",
				Description = description,
				RemotePath = entryPath,
				DestinationRoot = CopilotAgentsDestinationRoot,
				Files = [entryPath]
			});
		}

		return assets;
	}

	/// <summary>
	/// Installs an asset into the target project.
	/// </summary>
	public static async Task<(int FilesInstalled, string InstallPath)> InstallAssetAsync(
		HttpClient http,
		RepositoryAssetInfo asset,
		string projectRoot,
		string repo,
		string branch,
		bool force,
		CancellationToken ct = default)
	{
		var destinationRoot = Path.Combine(
			projectRoot,
			MarketplaceClient.NormalizePath(asset.DestinationRoot).Replace('/', Path.DirectorySeparatorChar));
		var destinationBase = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
		Directory.CreateDirectory(destinationRoot);

		var count = 0;
		foreach (var filePath in asset.Files)
		{
			var relativePath = GetRemoteFileName(filePath);
			var destinationPath = Path.Combine(destinationRoot, relativePath);
			var fullDestinationPath = Path.GetFullPath(destinationPath);
			if (!fullDestinationPath.StartsWith(destinationBase, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
				continue;

			if (File.Exists(fullDestinationPath) && !force)
				continue;

			var content = await MarketplaceClient.FetchRawBytesAsync(http, repo, branch, filePath, ct).ConfigureAwait(false);
			if (content is null)
				continue;

			await File.WriteAllBytesAsync(fullDestinationPath, content, ct).ConfigureAwait(false);
			count++;
		}

		return (count, destinationRoot);
	}

	static bool IsMauiRelatedAgent(string name, string? description, string content)
	{
		var haystack = string.Join('\n', name, description, content);
		return haystack.Contains("maui", StringComparison.OrdinalIgnoreCase) ||
			haystack.Contains("comet", StringComparison.OrdinalIgnoreCase);
	}

	static string GetRemoteFileName(string path)
	{
		var normalized = MarketplaceClient.NormalizePath(path);
		var slashIndex = normalized.LastIndexOf('/');
		return slashIndex >= 0 ? normalized[(slashIndex + 1)..] : normalized;
	}
}
