// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// HTTP client for marketplace operations. Fetches manifests, enumerates skills,
/// and downloads skill files from a GitHub-hosted marketplace repository.
/// </summary>
internal static class MarketplaceClient
{
	private const string GitHubApiBase = "https://api.github.com";
	private const string GitHubRawBase = "https://raw.githubusercontent.com";

	/// <summary>
	/// Fetches and deserializes the marketplace.json manifest from the repository.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/> (caller manages lifetime).</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <returns>The deserialized manifest, or <c>null</c> on failure.</returns>
	public static async Task<MarketplaceManifest?> GetMarketplaceAsync(HttpClient http, string repo, string branch, CancellationToken ct = default)
	{
		var url = $"{GitHubRawBase}/{repo}/{branch}/.github/plugin/marketplace.json";
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		return JsonSerializer.Deserialize(json, AiJsonContext.Default.MarketplaceManifest);
	}

	/// <summary>
	/// Fetches and deserializes the plugin.json manifest for a specific plugin.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="pluginSourcePath">Repository-relative path to the plugin directory.</param>
	/// <returns>The deserialized plugin manifest, or <c>null</c> on failure.</returns>
	public static async Task<PluginManifest?> GetPluginAsync(HttpClient http, string repo, string branch, string pluginSourcePath, CancellationToken ct = default)
	{
		var path = NormalizePath($"{pluginSourcePath}/plugin.json");
		var url = $"{GitHubRawBase}/{repo}/{branch}/{path}";
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		return JsonSerializer.Deserialize(json, AiJsonContext.Default.PluginManifest);
	}

	/// <summary>
	/// Fetches the full recursive tree for the given branch and returns the entries
	/// as (path, type) pairs. Results can be cached and passed to
	/// <see cref="GetSkillsAsync"/> to avoid redundant API calls.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>List of tree entries, or <c>null</c> on failure.</returns>
	public static async Task<List<(string Path, string Type)>?> FetchTreeEntriesAsync(
		HttpClient http, string repo, string branch, CancellationToken ct = default)
	{
		var treeSha = await ResolveTreeShaAsync(http, repo, branch, ct).ConfigureAwait(false);
		if (treeSha is null)
			return null;

		var treeUrl = $"{GitHubApiBase}/repos/{repo}/git/trees/{treeSha}?recursive=1";
		var treeJson = await FetchStringAsync(http, treeUrl, ct).ConfigureAwait(false);
		if (treeJson is null)
			return null;

		var treeNode = JsonNode.Parse(treeJson);
		var treeArray = treeNode?["tree"]?.AsArray();
		if (treeArray is null)
			return null;

		var entries = new List<(string Path, string Type)>();
		foreach (var entry in treeArray)
		{
			var entryPath = entry?["path"]?.GetValue<string>();
			var entryType = entry?["type"]?.GetValue<string>();
			if (entryPath is not null && entryType is not null)
				entries.Add((entryPath, entryType));
		}

		return entries;
	}

	/// <summary>
	/// Discovers all skills within a plugin by enumerating the repository tree
	/// and parsing SKILL.md frontmatter.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="plugin">The plugin manifest whose skills to discover.</param>
	/// <param name="pluginSourcePath">Repository-relative path to the plugin directory.</param>
	/// <param name="cachedTreeEntries">
	/// Optional pre-fetched tree entries from <see cref="FetchTreeEntriesAsync"/>.
	/// When <c>null</c>, the tree is fetched automatically (one API call per invocation).
	/// </param>
	/// <returns>List of discovered skills (empty on failure).</returns>
	public static async Task<List<SkillInfo>> GetSkillsAsync(
		HttpClient http, string repo, string branch, PluginManifest plugin, string pluginSourcePath,
		List<(string Path, string Type)>? cachedTreeEntries = null, CancellationToken ct = default)
	{
		var skills = new List<SkillInfo>();

		var entries = cachedTreeEntries ?? await FetchTreeEntriesAsync(http, repo, branch, ct).ConfigureAwait(false);
		if (entries is null)
			return skills;

		var normalizedPluginPath = NormalizePath(pluginSourcePath);

		foreach (var skillGlob in plugin.Skills)
		{
			var basePath = NormalizePath($"{normalizedPluginPath}/{skillGlob}");
			var prefix = basePath + "/";

			// Find SKILL.md files exactly one level below the base path.
			var skillMdPaths = new List<string>();
			foreach (var (entryPath, entryType) in entries)
			{
				if (entryType != "blob" || !entryPath.StartsWith(prefix, StringComparison.Ordinal))
					continue;

				var relative = entryPath[prefix.Length..];
				var slashIndex = relative.IndexOf('/');
				if (slashIndex > 0 && relative[(slashIndex + 1)..].Equals("SKILL.md", StringComparison.OrdinalIgnoreCase))
					skillMdPaths.Add(entryPath);
			}

			foreach (var skillMdPath in skillMdPaths)
			{
				var skillDir = skillMdPath[..skillMdPath.LastIndexOf('/')];
				var skillDirPrefix = skillDir + "/";

				// Collect all blob entries in this skill directory.
				var skillFiles = entries
					.Where(e => e.Type == "blob" && e.Path.StartsWith(skillDirPrefix, StringComparison.Ordinal))
					.Select(e => e.Path)
					.ToList();

				var (name, description) = await ParseSkillFrontmatterAsync(http, repo, branch, skillMdPath, ct).ConfigureAwait(false);
				var dirName = skillDir.Contains('/')
					? skillDir[(skillDir.LastIndexOf('/') + 1)..]
					: skillDir;

				skills.Add(new SkillInfo
				{
					Name = name ?? dirName,
					Description = description,
					PluginName = plugin.Name,
					RemotePath = skillDir,
					Files = skillFiles
				});
			}
		}

		return skills;
	}

	/// <summary>
	/// Downloads all files for a skill to the specified destination directory.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="skill">Skill whose files to download.</param>
	/// <param name="destDir">Local directory to write files into.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name to read from.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns>Count of files successfully downloaded.</returns>
	public static async Task<int> DownloadSkillFilesAsync(
		HttpClient http, SkillInfo skill, string destDir, string repo, string branch, CancellationToken ct = default)
	{
		var count = 0;
		var fullBase = Path.GetFullPath(destDir) + Path.DirectorySeparatorChar;

		foreach (var filePath in skill.Files)
		{
			// Compute the path relative to the skill's remote root.
			var relativePath = filePath;
			var remotePrefix = skill.RemotePath + "/";
			if (filePath.StartsWith(remotePrefix, StringComparison.Ordinal))
				relativePath = filePath[remotePrefix.Length..];

			var url = $"{GitHubRawBase}/{repo}/{branch}/{filePath}";
			var content = await FetchBytesAsync(http, url, ct).ConfigureAwait(false);
			if (content is null)
				continue;

			var destPath = Path.Combine(destDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

			// Validate the resolved path stays under the destination directory.
			var fullDest = Path.GetFullPath(destPath);
			if (!fullDest.StartsWith(fullBase, StringComparison.Ordinal))
				continue;

			var destFileDir = Path.GetDirectoryName(destPath);
			if (destFileDir is not null)
				Directory.CreateDirectory(destFileDir);

			await File.WriteAllBytesAsync(destPath, content, ct).ConfigureAwait(false);
			count++;
		}

		return count;
	}

	/// <summary>
	/// Resolves the latest commit SHA that touched a specific path on the given branch.
	/// </summary>
	/// <param name="http">Configured <see cref="HttpClient"/>.</param>
	/// <param name="repo">Repository in "owner/repo" format.</param>
	/// <param name="branch">Branch name.</param>
	/// <param name="path">Repository-relative path to query.</param>
	/// <returns>The commit SHA, or <c>null</c> on failure.</returns>
	public static async Task<string?> GetRemoteCommitShaAsync(HttpClient http, string repo, string branch, string path, CancellationToken ct = default)
	{
		var url = $"{GitHubApiBase}/repos/{repo}/commits?sha={Uri.EscapeDataString(branch)}&path={Uri.EscapeDataString(path)}&per_page=1";
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		var array = JsonNode.Parse(json)?.AsArray();
		return array is { Count: > 0 } ? array[0]?["sha"]?.GetValue<string>() : null;
	}

	/// <summary>
	/// Resolves the tree SHA for the given branch by fetching the latest commit.
	/// </summary>
	private static async Task<string?> ResolveTreeShaAsync(HttpClient http, string repo, string branch, CancellationToken ct = default)
	{
		var url = $"{GitHubApiBase}/repos/{repo}/commits/{Uri.EscapeDataString(branch)}";
		var json = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (json is null)
			return null;

		var node = JsonNode.Parse(json);
		return node?["commit"]?["tree"]?["sha"]?.GetValue<string>();
	}

	/// <summary>
	/// Downloads and parses the YAML frontmatter from a SKILL.md file.
	/// </summary>
	private static async Task<(string? Name, string? Description)> ParseSkillFrontmatterAsync(
		HttpClient http, string repo, string branch, string skillMdPath, CancellationToken ct = default)
	{
		var url = $"{GitHubRawBase}/{repo}/{branch}/{skillMdPath}";
		var content = await FetchStringAsync(http, url, ct).ConfigureAwait(false);
		if (content is null)
			return (null, null);

		return ParseFrontmatter(content);
	}

	/// <summary>
	/// Extracts name and description from YAML frontmatter delimited by <c>---</c>.
	/// Uses simple string operations — no YAML library required.
	/// </summary>
	internal static (string? Name, string? Description) ParseFrontmatter(string content)
	{
		string? name = null;
		string? description = null;

		var trimmed = content.TrimStart();
		if (!trimmed.StartsWith("---", StringComparison.Ordinal))
			return (name, description);

		var endIndex = trimmed.IndexOf("---", 3, StringComparison.Ordinal);
		if (endIndex < 0)
			return (name, description);

		var frontmatter = trimmed[3..endIndex];
		foreach (var line in frontmatter.Split('\n'))
		{
			var trimmedLine = line.Trim();
			if (trimmedLine.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
				name = StripYamlValue(trimmedLine["name:".Length..]);
			else if (trimmedLine.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
				description = StripYamlValue(trimmedLine["description:".Length..]);
		}

		return (name, description);
	}

	/// <summary>
	/// Strips surrounding whitespace and optional quotes from a YAML value.
	/// </summary>
	private static string StripYamlValue(string raw)
	{
		var value = raw.Trim();
		if (value.Length >= 2 &&
			((value[0] == '"' && value[^1] == '"') ||
			 (value[0] == '\'' && value[^1] == '\'')))
		{
			value = value[1..^1];
		}

		return value;
	}

	/// <summary>
	/// Normalizes a repository-relative path by removing <c>./</c> prefixes,
	/// collapsing double slashes, and trimming trailing slashes.
	/// </summary>
	private static string NormalizePath(string path)
	{
		var normalized = path.Replace('\\', '/');
		while (normalized.StartsWith("./", StringComparison.Ordinal))
			normalized = normalized[2..];
		while (normalized.Contains("//"))
			normalized = normalized.Replace("//", "/");
		return normalized.TrimEnd('/');
	}

	private static async Task<string?> FetchStringAsync(HttpClient http, string url, CancellationToken ct = default)
	{
		try
		{
			using var response = await http.GetAsync(url, ct).ConfigureAwait(false);

			// Return null only for 404 (resource not found); propagate other errors
			// so callers can surface meaningful messages.
			if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
				return null;

			response.EnsureSuccessStatusCode();

			return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException) when (!ct.IsCancellationRequested)
		{
			return null; // real HTTP timeout
		}
	}

	private static async Task<byte[]?> FetchBytesAsync(HttpClient http, string url, CancellationToken ct = default)
	{
		try
		{
			using var response = await http.GetAsync(url, ct).ConfigureAwait(false);

			if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
				return null;

			response.EnsureSuccessStatusCode();

			return await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
		}
		catch (HttpRequestException)
		{
			return null;
		}
		catch (TaskCanceledException) when (!ct.IsCancellationRequested)
		{
			return null; // real HTTP timeout
		}
	}
}
