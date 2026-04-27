// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Maui.Cli.Errors;
using Microsoft.Maui.Cli.Providers.Android;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class JdkManagerExtractionTests : IDisposable
{
	readonly string _tempDir;

	public JdkManagerExtractionTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"jdk-extract-test-{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_tempDir, recursive: true); }
		catch { /* best effort */ }
	}

	[Fact]
	public void ValidateExtractedPaths_AllFilesUnderDestination_DoesNotThrow()
	{
		// Arrange — normal directory structure
		Directory.CreateDirectory(Path.Combine(_tempDir, "bin"));
		File.WriteAllText(Path.Combine(_tempDir, "bin", "java"), "stub");
		File.WriteAllText(Path.Combine(_tempDir, "release"), "stub");

		// Act & Assert — no exception
		JdkManager.ValidateExtractedPaths(_tempDir);
	}

	[Fact]
	public void ValidateExtractedPaths_EmptyDirectory_DoesNotThrow()
	{
		// An empty extraction directory is harmless
		JdkManager.ValidateExtractedPaths(_tempDir);
	}

	[Fact]
	public void ValidateExtractedPaths_SymlinkOutsideDestination_Throws()
	{
		if (OperatingSystem.IsWindows())
			return; // Symlink creation requires privileges on Windows

		// Arrange — create a symlink that points outside the destination
		var outsideDir = Path.Combine(Path.GetTempPath(), $"jdk-outside-{Guid.NewGuid():N}");
		Directory.CreateDirectory(outsideDir);

		try
		{
			var symlinkPath = Path.Combine(_tempDir, "escape");
			Directory.CreateSymbolicLink(symlinkPath, outsideDir);

			// Act & Assert — validation detects the symlink target is outside destination
			var ex = Assert.Throws<MauiToolException>(() =>
				JdkManager.ValidateExtractedPaths(_tempDir));

			Assert.Equal(ErrorCodes.JdkInstallFailed, ex.Code);
			Assert.Contains("path traversal", ex.Message, StringComparison.OrdinalIgnoreCase);

			// The method deletes the destination on traversal detection
			Assert.False(Directory.Exists(_tempDir));
		}
		finally
		{
			try { Directory.Delete(outsideDir, recursive: true); }
			catch { /* best effort */ }
		}
	}

	[Fact]
	public void ValidateExtractedPaths_DeeplyNestedFiles_DoesNotThrow()
	{
		// Arrange — deeply nested but still under destination
		var deepPath = Path.Combine(_tempDir, "a", "b", "c", "d", "e");
		Directory.CreateDirectory(deepPath);
		File.WriteAllText(Path.Combine(deepPath, "file.txt"), "content");

		// Act & Assert — no exception
		JdkManager.ValidateExtractedPaths(_tempDir);
	}
}
