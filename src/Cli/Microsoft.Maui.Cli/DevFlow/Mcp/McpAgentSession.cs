using Microsoft.Maui.Cli.DevFlow.Broker;
using Microsoft.Maui.Cli.DevFlow.Android;
using Microsoft.Maui.DevFlow.Driver;

namespace Microsoft.Maui.Cli.DevFlow.Mcp;

public class McpAgentSession
{
	public int? DefaultAgentPort { get; set; }
	public string DefaultAgentHost { get; set; } = "localhost";

	public async Task<AgentClient> GetAgentClientAsync(int? agentPort = null)
	{
		var port = agentPort ?? DefaultAgentPort ?? await ResolveAgentPortAsync();
		if (agentPort.HasValue && DefaultAgentHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
			await TryEnsureAndroidForwardingAsync([agentPort.Value], ensureBrokerReverse: false);
		return new AgentClient(DefaultAgentHost, port);
	}

	public async Task<int> GetBrokerPortAsync()
	{
		var port = await BrokerClient.EnsureBrokerRunningAsync();
		return port ?? BrokerServer.DefaultPort;
	}

	public async Task<AgentRegistration[]?> ListAgentsAsync()
	{
		var brokerPort = await GetBrokerPortAsync();
		return await BrokerClient.ListAgentsAsync(brokerPort);
	}

	private async Task<int> ResolveAgentPortAsync()
	{
		var agent = await BrokerClient.ResolveAgentForProjectAsync();
		if (agent != null)
		{
			if (IsAndroidAgent(agent))
				await TryEnsureAndroidForwardingAsync([agent.Port], ensureBrokerReverse: true);
			return agent.Port;
		}

		var fallbackPort = BrokerClient.ReadConfigPort() ?? 9223;
		await TryEnsureAndroidForwardingAsync([fallbackPort], ensureBrokerReverse: true);
		return fallbackPort;
	}

	static async Task TryEnsureAndroidForwardingAsync(int[] agentPorts, bool ensureBrokerReverse)
	{
		try
		{
			await AndroidDevFlowPortForwarder.CreateDefault().EnsureAsync(new AndroidDevFlowForwardingRequest
			{
				AgentPorts = agentPorts,
				EnsureBrokerReverse = ensureBrokerReverse,
				Repair = true
			});
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"[DevFlow Android forwarding] {ex.Message}");
		}
	}

	static bool IsAndroidAgent(AgentRegistration agent)
		=> agent.Platform.Contains("Android", StringComparison.OrdinalIgnoreCase)
		   || agent.Tfm.Contains("-android", StringComparison.OrdinalIgnoreCase);
}
