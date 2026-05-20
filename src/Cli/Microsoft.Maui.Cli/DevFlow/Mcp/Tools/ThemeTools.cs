using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Microsoft.Maui.Cli.DevFlow.Mcp;
using Microsoft.Maui.Cli.Utils;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp.Tools;

[McpServerToolType]
public sealed class ThemeTools
{
    [McpServerTool(Name = "maui_get_theme"),
     Description("Get the current app-scoped light/dark theme reported by the connected MAUI DevFlow agent.")]
    public static async Task<string> GetTheme(
        McpAgentSession session,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null)
    {
        using var agent = await session.GetAgentClientAsync(agentPort);
        var result = await agent.GetThemeAsync();
        if (result == null)
            throw new McpException("Unable to retrieve theme. The agent may not be running or may not support theme switching.");

        return CliJson.SerializeUntyped(result, indented: false);
    }

    [McpServerTool(Name = "maui_set_theme"),
     Description("Set the running app or disposable emulator/simulator to light, dark, or system theme. Scope defaults to auto: uses system scope only for Android emulators and iOS simulators, and app scope for desktops or physical devices.")]
    public static async Task<string> SetTheme(
        McpAgentSession session,
        [Description("Theme to apply: light, dark, or system. System means follow the OS in app scope; iOS Simulator host scope supports only light and dark.")] string theme,
        [Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
        [Description("Where to apply the theme: auto, app, or system. Auto uses host-side switching only for Android emulators and iOS simulators.")] string scope = "auto",
        [Description("Android adb device/emulator serial for system-scope changes. Optional when adb has exactly one target.")] string? androidDevice = null,
        [Description("iOS Simulator UDID for system-scope changes. If omitted for simulator targets, the booted simulator is auto-detected.")] string? simulatorUdid = null)
    {
        if (!ThemeExtensions.TryParseTheme(theme, out var parsedTheme))
            throw new McpException($"Invalid theme '{theme}'. Use light, dark, or system.");

        if (!ThemeExtensions.TryParseScope(scope, out var parsedScope))
            throw new McpException($"Invalid scope '{scope}'. Use auto, app, or system.");

        using var agent = await session.GetAgentClientAsync(agentPort);
        AgentStatus? status = null;
        if (parsedScope != ThemeSetScope.System)
            status = await agent.GetStatusAsync();

        var platform = status?.Platform;
        var deviceType = status?.DeviceType;
        var useHost = parsedScope == ThemeSetScope.System
            || ShouldUseHostThemeScopeAutomatically(platform, deviceType, parsedTheme, androidDevice, simulatorUdid);

        ThemeResult result = useHost
            ? await SetHostThemeAsync(platform, deviceType, parsedTheme, androidDevice, simulatorUdid)
            : await agent.SetThemeAsync(parsedTheme);

        if (!result.Success)
            throw new McpException(result.Message ?? "Failed to set theme.");

        return CliJson.SerializeUntyped(result, indented: false);
    }

    private static bool ShouldUseHostThemeScopeAutomatically(
        string? platform,
        string? deviceType,
        DevFlowTheme theme,
        string? androidDevice,
        string? simulatorUdid)
    {
        if (IsAndroidTarget(platform, androidDevice))
            return IsVirtualDevice(deviceType) || IsAndroidEmulatorSerial(androidDevice);

        if (theme == DevFlowTheme.System)
            return false;

        return IsIosSimulatorTarget(platform, deviceType, simulatorUdid);
    }

    private static async Task<ThemeResult> SetHostThemeAsync(
        string? platform,
        string? deviceType,
        DevFlowTheme theme,
        string? androidDevice,
        string? simulatorUdid)
    {
        if (IsAndroidTarget(platform, androidDevice))
        {
            var driver = new AndroidAppDriver { Serial = androidDevice };
            return await driver.SetThemeAsync(theme, ThemeSetScope.System);
        }

        if (IsIosSimulatorTarget(platform, deviceType, simulatorUdid))
        {
            var resolvedUdid = await ResolveUdidAsync(simulatorUdid);
            var driver = new iOSSimulatorAppDriver { DeviceUdid = resolvedUdid };
            return await driver.SetThemeAsync(theme, ThemeSetScope.System);
        }

        return new ThemeResult
        {
            Theme = theme,
            RequestedTheme = theme,
            Source = "system",
            Success = false,
            Message = $"System theme switching is not supported for platform '{platform ?? "unknown"}'. Use app scope.",
        };
    }

    private static async Task<string> ResolveUdidAsync(string? udid)
    {
        if (!string.IsNullOrWhiteSpace(udid))
            return udid;

        var result = await ProcessRunner.RunAsync("xcrun", ["simctl", "list", "devices", "booted", "-j"]);
        if (!result.Success)
            throw new McpException($"Failed to resolve simulator UDID: {result.StandardError.Trim()}");

        using var document = JsonDocument.Parse(result.StandardOutput);
        if (document.RootElement.TryGetProperty("devices", out var devices))
        {
            foreach (var runtime in devices.EnumerateObject())
            {
                foreach (var device in runtime.Value.EnumerateArray())
                {
                    var state = device.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
                    if (state == "Booted")
                        return device.GetProperty("udid").GetString()!;
                }
            }
        }

        throw new McpException("No booted simulator found. Pass simulatorUdid or boot a simulator.");
    }

    private static bool IsAndroidTarget(string? platform, string? androidDevice)
        => !string.IsNullOrWhiteSpace(androidDevice)
            || (platform?.Contains("android", StringComparison.OrdinalIgnoreCase) == true);

    private static bool IsIosSimulatorTarget(string? platform, string? deviceType, string? simulatorUdid)
    {
        if (!string.IsNullOrWhiteSpace(simulatorUdid))
            return true;

        if (!IsVirtualDevice(deviceType))
            return false;

        return platform?.Equals("ios", StringComparison.OrdinalIgnoreCase) == true
            || platform?.Contains("iossimulator", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsVirtualDevice(string? deviceType)
        => deviceType?.Equals("Virtual", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsAndroidEmulatorSerial(string? androidDevice)
        => androidDevice?.StartsWith("emulator-", StringComparison.OrdinalIgnoreCase) == true;
}
