// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Formats.Tar;
using System.IO.Compression;
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
	public void ValidateArchiveEntries_SafeArchive_DoesNotThrow()
	{
		var archivePath = CreateTarGz(
			("jdk-21/bin/java", "stub"),
			("jdk-21/release", "stub"),
			("jdk-21/lib/modules", "stub"));

		var dest = Path.Combine(_tempDir, "dest");
		Directory.CreateDirectory(dest);

		JdkManager.ValidateArchiveEntries(archivePath, dest, stripComponents: 1);
	}

	[Fact]
	public void ValidateArchiveEntries_TraversalEntry_Throws()
	{
		var archivePath = CreateTarGz(
			("jdk-21/../../etc/cron.d/backdoor", "payload"));

		var dest = Path.Combine(_tempDir, "dest");
		Directory.CreateDirectory(dest);

		var ex = Assert.Throws<MauiToolException>(() =>
			JdkManager.ValidateArchiveEntries(archivePath, dest, stripComponents: 1));

		Assert.Equal(ErrorCodes.JdkInstallFailed, ex.Code);
		Assert.Contains("path traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ValidateEntryPath_AbsolutePathEntry_Throws()
	{
		// System.Formats.Tar normalizes absolute paths by stripping leading '/',
		// so we test ValidateEntryPath directly to verify the guard works.
		var dest = Path.GetFullPath(Path.Combine(_tempDir, "dest")) + Path.DirectorySeparatorChar;
		var comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		var ex = Assert.Throws<MauiToolException>(() =>
			JdkManager.ValidateEntryPath(dest, comparison, "/etc/passwd", "/etc/passwd"));

		Assert.Equal(ErrorCodes.JdkInstallFailed, ex.Code);
		Assert.Contains("absolute path", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ValidateArchiveEntries_SymlinkOutsideDestination_Throws()
	{
		var archivePath = CreateTarGzWithSymlink(
			entryName: "jdk-21/escape",
			linkTarget: "/etc");

		var dest = Path.Combine(_tempDir, "dest");
		Directory.CreateDirectory(dest);

		var ex = Assert.Throws<MauiToolException>(() =>
			JdkManager.ValidateArchiveEntries(archivePath, dest, stripComponents: 1));

		Assert.Equal(ErrorCodes.JdkInstallFailed, ex.Code);
		Assert.Contains("symlink", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ValidateArchiveEntries_RelativeSymlinkTraversal_Throws()
	{
		var archivePath = CreateTarGzWithSymlink(
			entryName: "jdk-21/escape",
			linkTarget: "../../outside");

		var dest = Path.Combine(_tempDir, "dest");
		Directory.CreateDirectory(dest);

		var ex = Assert.Throws<MauiToolException>(() =>
			JdkManager.ValidateArchiveEntries(archivePath, dest, stripComponents: 1));

		Assert.Equal(ErrorCodes.JdkInstallFailed, ex.Code);
		Assert.Contains("symlink", ex.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ValidateArchiveEntries_EmptyArchive_DoesNotThrow()
	{
		var archivePath = CreateTarGz();

		var dest = Path.Combine(_tempDir, "dest");
		Directory.CreateDirectory(dest);

		JdkManager.ValidateArchiveEntries(archivePath, dest, stripComponents: 1);
	}

	[Fact]
	public void StripPathComponents_RemovesCorrectSegments()
	{
		Assert.Equal("bin/java", JdkManager.StripPathComponents("jdk-21/bin/java", 1));
		Assert.Equal("java", JdkManager.StripPathComponents("jdk-21/bin/java", 2));
		Assert.Null(JdkManager.StripPathComponents("jdk-21/", 1));
		Assert.Null(JdkManager.StripPathComponents("jdk-21", 1));
		Assert.Equal("jdk-21/bin/java", JdkManager.StripPathComponents("jdk-21/bin/java", 0));
	}

	string CreateTarGz(params (string name, string content)[] entries)
	{
		var path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.tar.gz");
		using var fileStream = File.Create(path);
		using var gzipStream = new GZipStream(fileStream, CompressionMode.Compress);
		using var writer = new TarWriter(gzipStream, leaveOpen: false);

		foreach (var (name, content) in entries)
		{
			var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
			{
				DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))
			};
			writer.WriteEntry(entry);
		}

		return path;
	}

	string CreateTarGzWithSymlink(string entryName, string linkTarget)
	{
		var path = Path.Combine(_tempDir, $"test-{Guid.NewGuid():N}.tar.gz");
		using var fileStream = File.Create(path);
		using var gzipStream = new GZipStream(fileStream, CompressionMode.Compress);
		using var writer = new TarWriter(gzipStream, leaveOpen: false);

		var entry = new PaxTarEntry(TarEntryType.SymbolicLink, entryName)
		{
			LinkName = linkTarget
		};
		writer.WriteEntry(entry);

		return path;
	}
}
