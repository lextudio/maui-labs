using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;
using Microsoft.Maui.DevFlow.CLI.Mcp;

namespace Microsoft.Maui.DevFlow.CLI.Mcp.Tools;

[McpServerToolType]
public sealed class ScreenshotTool
{
	[McpServerTool(Name = "maui_screenshot"), Description("Capture a screenshot of the running MAUI app. Returns the image directly for visual verification of layout, colors, contrast, and rendering. By default captures the Page content area only. Use fullscreen=true to capture the complete device display including status bar, navigation bar, and safe area regions — use this when verifying layout near screen edges, notch/Dynamic Island areas, or safe area insets.")]
	public static async Task<ContentBlock[]> Screenshot(
		McpAgentSession session,
		[Description("Agent HTTP port (optional if only one agent connected)")] int? agentPort = null,
		[Description("Window index for multi-window apps (default: 0)")] int? window = null,
		[Description("Element ID to capture a specific element")] string? elementId = null,
		[Description("CSS selector to capture (first match, Blazor WebViews only)")] string? selector = null,
		[Description("Capture the complete device display including status bar, home indicator, and safe area regions. Use when verifying edge layout, safe areas, notch clearance, or content near screen boundaries. Default: false (captures Page content area only)")] bool fullscreen = false,
		[Description("Resize screenshot to this max width in pixels (overrides auto-scaling)")] int? maxWidth = null,
		[Description("Scale mode: 'native' keeps full HiDPI resolution, default auto-scales to 1x logical pixels")] string? scale = null)
	{
		if (fullscreen && (!string.IsNullOrWhiteSpace(elementId) || !string.IsNullOrWhiteSpace(selector)))
			throw new McpException("'fullscreen' and 'elementId'/'selector' are mutually exclusive. Omit elementId/selector for a fullscreen screenshot, or set fullscreen=false to capture a specific element.");

		var agent = await session.GetAgentClientAsync(agentPort);
		var bytes = await agent.ScreenshotAsync(window, elementId, selector, maxWidth, scale, fullscreen);
		if (bytes == null || bytes.Length == 0)
			throw new McpException("Screenshot failed — no image data returned. Is the agent connected and the app visible?");

		var modeLabel = fullscreen ? "fullscreen" : !string.IsNullOrWhiteSpace(elementId) || !string.IsNullOrWhiteSpace(selector) ? "element" : "page";
		return [
			new TextContentBlock { Text = $"Screenshot captured ({bytes.Length} bytes, PNG, mode: {modeLabel})" },
			ImageContentBlock.FromBytes(bytes, "image/png")
		];
	}
}
