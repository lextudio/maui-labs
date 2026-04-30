using System.Diagnostics;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Fixture that builds and launches the DevFlow sample app as a Mac Catalyst app.
/// </summary>
public sealed class MacCatalystFixture : AppFixtureBase
{
    Process? _appProcess;
    bool _weOwnTheProcess;

    public override string Platform => "maccatalyst";

    protected override async Task InitializePlatformAsync()
    {
        await WithBuildLockAsync(async () =>
        {
            // Sample is built by MSBuild as a no-op ProjectReference when this test
            // project is built with -p:DevFlowIntegrationPlatform=maccatalyst. The
            // fixture only needs to locate the resulting .app bundle.
            var appPath = FindAppBundle();
            LaunchApp(appPath);
            _weOwnTheProcess = true;
        });
    }

    protected override async Task DisposePlatformAsync()
    {
        if (_weOwnTheProcess && _appProcess is { HasExited: false })
        {
            _appProcess.Kill(entireProcessTree: true);
            try { await _appProcess.WaitForExitAsync(new CancellationTokenSource(5000).Token); } catch { }
        }

        _appProcess?.Dispose();
    }

    static string FindAppBundle()
    {
        var prebuiltPath = GetPrebuiltSampleAppPath();
        if (prebuiltPath != null)
            return FindAppBundle(prebuiltPath);

        var sampleBinDir = Path.Combine(GetSampleBuildOutputRoot(), "net10.0-maccatalyst");
        return FindAppBundle(sampleBinDir);
    }

    static string FindAppBundle(string path)
    {
        if (Directory.Exists(path) && Path.GetExtension(path).Equals(".app", StringComparison.OrdinalIgnoreCase))
            return path;

        if (!Directory.Exists(path))
            throw new InvalidOperationException($"Build output directory not found: {path}");

        var appBundles = Directory.GetDirectories(path, "*.app", SearchOption.AllDirectories);

        if (appBundles.Length == 0)
            throw new InvalidOperationException($"No .app bundle found under {path}");

        return appBundles[0];
    }

    void LaunchApp(string appBundlePath)
    {
        var macosDir = Path.Combine(appBundlePath, "Contents", "MacOS");

        if (!Directory.Exists(macosDir))
            throw new InvalidOperationException($"MacOS directory not found at: {macosDir}");

        var executables = Directory.GetFiles(macosDir)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .ToArray();

        if (executables.Length == 0)
            throw new InvalidOperationException($"No executables found in {macosDir}");

        var executablePath = executables[0];

        var psi = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        psi.Environment["DEVFLOW_TEST_PORT"] = AgentPort.ToString();

        _appProcess = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch {executablePath}");
    }
}
