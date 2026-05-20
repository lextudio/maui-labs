using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.DevFlow.Tests;

public class ThemeAgentClientTests
{
    [Fact]
    public async Task Theme_GetSetFlow_WorksThroughAgentClient()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                using var client = await listener.AcceptTcpClientAsync();
                using var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);

                if (request.Contains("GET /api/v1/device/app/theme", StringComparison.Ordinal))
                {
                    await WriteJsonResponseAsync(stream, """
                    {
                      "theme": "dark",
                      "requestedTheme": "dark",
                      "userAppTheme": "dark",
                      "effectiveTheme": "dark",
                      "supportedThemes": ["light", "dark", "system"],
                      "source": "app"
                    }
                    """);
                    continue;
                }

                if (request.Contains("PUT /api/v1/device/app/theme", StringComparison.Ordinal))
                {
                    Assert.Contains("\"theme\":\"light\"", request);
                    await WriteJsonResponseAsync(stream, """
                    {
                      "theme": "light",
                      "requestedTheme": "light",
                      "userAppTheme": "light",
                      "effectiveTheme": "light",
                      "supportedThemes": ["light", "dark", "system"],
                      "source": "app"
                    }
                    """);
                    continue;
                }

                throw new InvalidOperationException($"Unexpected request: {request}");
            }
        });

        using var agent = new AgentClient("localhost", port);

        var current = await agent.GetThemeAsync();
        Assert.NotNull(current);
        Assert.Equal(DevFlowTheme.Dark, current.Theme);
        Assert.Equal(DevFlowTheme.Dark, current.EffectiveTheme);
        Assert.True(current.Success);

        var updated = await agent.SetThemeAsync(DevFlowTheme.Light);
        Assert.Equal(DevFlowTheme.Light, updated.Theme);
        Assert.Equal(DevFlowTheme.Light, updated.UserAppTheme);
        Assert.Equal("app", updated.Source);
        Assert.True(updated.Success);

        await serverTask;
    }

    private static async Task<string> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[8192];
        var builder = new StringBuilder();
        int read;
        do
        {
            read = await stream.ReadAsync(buffer);
            builder.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
        while (stream.DataAvailable);

        return builder.ToString();
    }

    private static async Task WriteJsonResponseAsync(NetworkStream stream, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header);
        await stream.WriteAsync(bytes);
    }
}
