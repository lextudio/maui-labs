// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Ai;
using Microsoft.Maui.Cli.Ai.Models;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class SkillInstallerTests : IDisposable
{
	private readonly string _tempDir;

	public SkillInstallerTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		if (Directory.Exists(_tempDir))
			Directory.Delete(_tempDir, recursive: true);
	}

	[Fact]
	public async Task InstallSkillAsync_InvalidName_PathTraversal_ReturnsNegativeOne()
	{
		var skill = new SkillInfo { Name = "../escape" };
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(_tempDir, "skills")
		};

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			new HttpClient(), skill, env, _tempDir, "owner/repo", "main", force: false);

		Assert.Equal(-1, filesInstalled);
		Assert.Equal(string.Empty, installPath);
	}

	[Fact]
	public async Task InstallSkillAsync_InvalidName_PathSeparator_ReturnsNegativeOne()
	{
		var separator = Path.DirectorySeparatorChar.ToString();
		var skill = new SkillInfo { Name = $"bad{separator}name" };
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(_tempDir, "skills")
		};

		var (filesInstalled, installPath) = await SkillInstaller.InstallSkillAsync(
			new HttpClient(), skill, env, _tempDir, "owner/repo", "main", force: false);

		Assert.Equal(-1, filesInstalled);
		Assert.Equal(string.Empty, installPath);
	}

	[Fact]
	public async Task InstallSkillAsync_ValidName_DoesNotReturnNegativeOne()
	{
		var skill = new SkillInfo
		{
			Name = "valid-skill",
			RemotePath = ".github/plugins/maui/skills/valid-skill",
			Files = ["file1.md"]
		};
		var env = new DetectedEnvironment
		{
			Kind = AgentEnvironmentKind.Claude,
			SkillsDirectory = Path.Combine(_tempDir, "skills")
		};

		// This will fail at the HTTP level (no real server), but it should NOT
		// return -1 because the name validation passes. We expect either an
		// exception from the HTTP call or a -2 (download returned 0 files).
		try
		{
			var (filesInstalled, _) = await SkillInstaller.InstallSkillAsync(
				new HttpClient(), skill, env, _tempDir, "owner/repo", "main", force: false);

			// If it didn't throw, it should not be -1 (invalid name)
			Assert.NotEqual(-1, filesInstalled);
		}
		catch (HttpRequestException)
		{
			// Expected: the HttpClient has no BaseAddress so the HTTP call fails.
			// The important thing is we got past the name validation.
		}
		catch (InvalidOperationException)
		{
			// Also acceptable: HttpClient may throw this without a BaseAddress.
		}
	}
}
