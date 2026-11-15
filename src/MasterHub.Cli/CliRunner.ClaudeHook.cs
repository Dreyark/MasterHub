using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Api.Client;
using MasterHub.Core.Status;

namespace MasterHub.Cli;

/// <summary>
/// <c>masterhub-cli --claude-hook</c> (T-25): Claude Code calls it from its hooks and pipes the hook's JSON to its input. It forwards
/// <b>four fields only</b> – session id, project folder, event name and (for a permission) the tool name – and <b>never</b> a prompt, a tool input or
/// a message: nothing else is even copied out of the input.
/// It must never get in Claude Code's way: it prints nothing (hook output can reach the model), always returns 0 and gives up quickly when the
/// app is not running.
/// </summary>
public sealed partial class CliRunner
{
    /// <summary>The longest hook input it reads (a pasted prompt can be large; it is only skipped over, never kept).</summary>
    public const int MaxHookInputChars = 8 * 1024 * 1024;

    /// <summary>The fields of the hook input that are forwarded. Everything else in it is ignored.</summary>
    internal static AgentRequest? ReadHookInput(TextReader input)
    {
        string json;
        try
        {
            var text = new System.Text.StringBuilder();
            var chunk = new char[8192];
            int read;
            while ((read = input.Read(chunk, 0, chunk.Length)) > 0)
            {
                text.Append(chunk, 0, read);
                if (text.Length > MaxHookInputChars) return null;
            }
            json = text.ToString();
        }
        catch { return null; }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            string Text(string name) => root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";

            string hookEvent = Text("hook_event_name");
            return new AgentRequest
            {
                Session = Text("session_id"),
                Project = AgentTracker.FolderName(Text("cwd")),   // the folder name, not the path
                Event = hookEvent,
                // only a permission has a tool worth showing; for the other events the tool is not even looked at
                Tool = hookEvent is "PermissionRequest" or "Notification" ? Text("tool_name") : null
            };
        }
        catch (JsonException) { return null; }
    }

    private async Task<int> ClaudeHookAsync(TextReader input, CancellationToken ct)
    {
        var request = ReadHookInput(input);
        if (request == null) return Ok;

        try
        {
            using var client = _connect();
            using var quick = CancellationTokenSource.CreateLinkedTokenSource(ct);
            quick.CancelAfter(TimeSpan.FromSeconds(3));
            await client.PostAgentAsync(request, quick.Token);
        }
        catch
        {
            // not running, too old, refused: the hook stays silent and Claude Code goes on
        }

        return Ok;
    }
}
