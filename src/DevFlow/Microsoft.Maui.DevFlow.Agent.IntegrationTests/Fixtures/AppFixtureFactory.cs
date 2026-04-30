using System.Reflection;

namespace Microsoft.Maui.DevFlow.Agent.IntegrationTests.Fixtures;

/// <summary>
/// Factory that creates the appropriate platform fixture. The target platform is
/// stamped into the test assembly at build time as [AssemblyMetadata("DevFlowTestPlatform", "...")]
/// by IntegrationTests.Common.props, so each platform-specific test csproj
/// (Android/iOS/MacCatalyst/Windows) self-identifies. The DEVFLOW_TEST_PLATFORM
/// environment variable is honored as an override for ad-hoc runs.
/// </summary>
public static class AppFixtureFactory
{
    public static IAppFixture Create()
    {
        var platform = Environment.GetEnvironmentVariable("DEVFLOW_TEST_PLATFORM")?.ToLowerInvariant();

        if (string.IsNullOrEmpty(platform))
        {
            platform = typeof(AppFixtureFactory).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => string.Equals(a.Key, "DevFlowTestPlatform", StringComparison.Ordinal))
                ?.Value
                ?.ToLowerInvariant();
        }

        if (string.IsNullOrEmpty(platform))
        {
            platform = OperatingSystem.IsWindows() ? "windows" : "maccatalyst";
        }

        return platform switch
        {
            "maccatalyst" or "mac" or "catalyst" => new MacCatalystFixture(),
            "ios" => new iOSSimulatorFixture(),
            "android" => new AndroidEmulatorFixture(),
            "windows" => new WindowsFixture(),
            _ => throw new InvalidOperationException(
                $"Unknown test platform '{platform}'. " +
                "Supported values: maccatalyst, ios, android, windows. " +
                "Set the DEVFLOW_TEST_PLATFORM environment variable.")
        };
    }
}
