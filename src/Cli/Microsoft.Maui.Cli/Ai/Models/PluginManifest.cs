// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.Maui.Cli.Ai.Models;

/// <summary>
/// Represents the plugin.json manifest for a single plugin in the marketplace.
/// </summary>
internal sealed class PluginManifest
{
	/// <summary>
	/// Name of the plugin.
	/// </summary>
	public string Name { get; set; } = string.Empty;

	/// <summary>
	/// Version of the plugin (e.g. "0.1.0").
	/// </summary>
	public string? Version { get; set; }

	/// <summary>
	/// Optional description of the plugin.
	/// </summary>
	public string? Description { get; set; }

	/// <summary>
	/// Relative paths to skill directories or skill definition globs.
	/// </summary>
	public string[] Skills { get; set; } = [];
}
