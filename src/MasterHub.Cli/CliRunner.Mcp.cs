using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Cli.Mcp;
using MasterHub.Core.Api.Client;

namespace MasterHub.Cli;

public sealed partial class CliRunner
{
    /// <summary>Set to <c>1</c> by the user (in the MCP server's environment) to let the assistant use the two actions it is normally refused. The model cannot set it.</summary>
    public const string MorePowerVariable = "MASTERHUB_MCP_MORE_POWER";

    private async Task<int> McpAsync(TextWriter output, CancellationToken ct)
    {
        // The answers go to the output; nothing else may be written there or the client could not read the stream.
        var server = new McpServer(() => _connectAssistant());
        await server.RunAsync(_input, output, ct);
        return Ok;
    }

    private static ILocalApiClient ConnectAsAssistant()
    {
        bool morePower = Environment.GetEnvironmentVariable(MorePowerVariable) == "1";
        return new LocalApiClient(ApiConnection.Read(), assistant: morePower ? "assistant-power" : "assistant");
    }
}
