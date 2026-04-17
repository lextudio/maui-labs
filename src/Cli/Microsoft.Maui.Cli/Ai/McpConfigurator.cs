// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Maui.Cli.Ai.Models;

namespace Microsoft.Maui.Cli.Ai;

/// <summary>
/// Writes MCP server configuration for agent environments. Performs a
/// schema-preserving merge so existing configuration entries are retained.
/// </summary>
internal static class McpConfigurator
{
	private const string ServerName = "maui-devflow";

	/// <summary>
	/// Ensures the <c>maui-devflow</c> MCP server entry exists in the
	/// environment's MCP configuration file. Creates the file if it does not exist.
	/// The operation is idempotent — it does nothing if the entry already exists.
	/// </summary>
	/// <param name="env">Target agent environment.</param>
	/// <param name="ct">Cancellation token.</param>
	/// <returns><c>true</c> if the configuration is in place; <c>false</c> on failure.</returns>
	public static async Task<bool> ConfigureAsync(DetectedEnvironment env, CancellationToken ct = default)
	{
		try
		{
			var configPath = env.McpConfigPath;

			JsonObject root;
			if (File.Exists(configPath))
			{
				var existingJson = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
				root = JsonNode.Parse(existingJson) as JsonObject ?? new JsonObject();
			}
			else
			{
				root = new JsonObject();
			}

			var serverEntry = new JsonObject
			{
				["command"] = "maui",
				["args"] = new JsonArray("devflow", "mcp")
			};

			bool alreadyConfigured;
			if (env.Kind == AgentEnvironmentKind.OpenCode)
			{
				alreadyConfigured = EnsureOpenCodeEntry(root, serverEntry);
			}
			else
			{
				alreadyConfigured = EnsureStandardEntry(root, serverEntry);
			}

			if (alreadyConfigured)
				return true;

			// Ensure the config directory exists before writing.
			var configDir = Path.GetDirectoryName(configPath);
			if (configDir is not null)
				Directory.CreateDirectory(configDir);

			var options = new JsonSerializerOptions { WriteIndented = true };
			await File.WriteAllTextAsync(configPath, root.ToJsonString(options), ct).ConfigureAwait(false);

			return true;
		}
		catch (IOException)
		{
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	/// <summary>
	/// Adds the server entry under the standard <c>mcpServers</c> key used by
	/// Claude, VS Code, and Copilot CLI.
	/// </summary>
	/// <returns><c>true</c> if the entry already exists (no changes needed).</returns>
	private static bool EnsureStandardEntry(JsonObject root, JsonObject serverEntry)
	{
		var existing = root["mcpServers"];
		if (existing is not null and not JsonObject)
			return false;

		if (existing is not JsonObject mcpServers)
		{
			mcpServers = new JsonObject();
			root["mcpServers"] = mcpServers;
		}

		if (mcpServers[ServerName] is not null)
			return true;

		mcpServers[ServerName] = serverEntry;
		return false;
	}

	/// <summary>
	/// Adds the server entry under the OpenCode-specific <c>mcp.servers</c> key.
	/// </summary>
	/// <returns><c>true</c> if the entry already exists (no changes needed).</returns>
	private static bool EnsureOpenCodeEntry(JsonObject root, JsonObject serverEntry)
	{
		var existingMcp = root["mcp"];
		if (existingMcp is not null and not JsonObject)
			return false;

		if (existingMcp is not JsonObject mcp)
		{
			mcp = new JsonObject();
			root["mcp"] = mcp;
		}

		var existingServers = mcp["servers"];
		if (existingServers is not null and not JsonObject)
			return false;

		if (existingServers is not JsonObject servers)
		{
			servers = new JsonObject();
			mcp["servers"] = servers;
		}

		if (servers[ServerName] is not null)
			return true;

		servers[ServerName] = serverEntry;
		return false;
	}
}
