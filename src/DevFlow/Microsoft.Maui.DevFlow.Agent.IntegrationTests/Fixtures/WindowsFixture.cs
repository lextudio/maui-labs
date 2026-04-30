using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app on Windows.
/// </summary>
public sealed class WindowsFixture : AppFixtureBase
{
    Process? _appProcess;

    public override string Platform => "windows";

    protected override async Task InitializePlatformAsync()
    {
        await WithBuildLockAsync(async () =>
        {
            // Sample is built by MSBuild as a no-op ProjectReference when this test
            // project is built with -p:DevFlowIntegrationPlatform=windows. The fixture
            // only needs to locate the resulting executable.
            var exePath = FindExecutable();
            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory,
            };

            psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

            _appProcess = Process.Start(psi)
                ?? throw new InvalidOperationException($"Failed to launch {exePath}");
        });
    }

    protected override async Task DisposePlatformAsync()
    {
        if (_appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            try { await _appProcess.WaitForExitAsync(new CancellationTokenSource(5000).Token); } catch { }
        }

        _appProcess?.Dispose();
    }

    static string FindExecutable()
    {
        var prebuiltPath = GetPrebuiltSampleAppPath();
        if (prebuiltPath != null)
            return FindExecutable(prebuiltPath);

        var binDir = GetSampleBuildOutputRoot();
        return FindExecutable(binDir);
    }

    static string FindExecutable(string path)
    {
        if (File.Exists(path))
        {
            if (Path.GetFileName(path).Equals("DevFlow.Sample.exe", StringComparison.OrdinalIgnoreCase))
                return path;

            throw new InvalidOperationException($"Prebuilt Windows sample path is not DevFlow.Sample.exe: {path}");
        }

        if (!Directory.Exists(path))
            throw new InvalidOperationException($"Windows build output not found at: {path}");

        var exes = Directory.GetFiles(path, "DevFlow.Sample.exe", SearchOption.AllDirectories);

        if (exes.Length == 0)
            throw new InvalidOperationException($"No DevFlow.Sample.exe found under {path}");

        return exes[0];
    }
}
