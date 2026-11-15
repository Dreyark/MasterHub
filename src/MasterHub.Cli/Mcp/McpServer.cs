using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Actions;
using MasterHub.Core.Api;
using MasterHub.Core.Api.Client;

namespace MasterHub.Cli.Mcp;

/// <summary>
/// <c>masterhub-cli --mcp</c> (T-26): a Model Context Protocol server over stdio (JSON-RPC 2.0, one message per line) that lets an AI
/// assistant design the deck. It only bridges to the running app's local API – it never touches the deck or the profile files – and it
/// <b>re-reads the port and token on every call</b>, so an app that restarted is found again. Every request it makes says it comes from an
/// assistant, so the app applies the assistant's limits (see <see cref="AssistantGuard"/>); the server cannot lift them, and there is no tool
/// that reaches the hardware directly.
/// </summary>
public sealed class McpServer
{
    /// <summary>Protocol versions it speaks, newest first. A client that asks for one of them gets it echoed.</summary>
    public static readonly string[] SupportedVersions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };

    public const string DesignGuideUri = "masterhub://design-guide";

    private readonly Func<ILocalApiClient> _connect;

    /// <param name="connect">Opens a connection to the app (reads port and token). Called for every tool call.</param>
    public McpServer(Func<ILocalApiClient> connect) => _connect = connect ?? throw new ArgumentNullException(nameof(connect));

    /// <summary>Reads one message per line until the input ends.</summary>
    public async Task RunAsync(TextReader input, TextWriter output, CancellationToken ct = default)
    {
        string? line;
        while (!ct.IsCancellationRequested && (line = await input.ReadLineAsync(ct)) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? answer = await HandleAsync(line, ct);
            if (answer == null) continue;   // a notification: no answer
            await output.WriteLineAsync(answer);
            await output.FlushAsync(ct);
        }
    }

    /// <summary>Handles one message. Returns the JSON answer, or <c>null</c> for a notification.</summary>
    public async Task<string?> HandleAsync(string message, CancellationToken ct = default)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(message); }
        catch (JsonException) { return Error(null, -32700, "Parse error"); }

        if (root is not JsonObject request) return Error(null, -32600, "Invalid request: a JSON object is expected");

        var id = request["id"]?.DeepClone();
        string? method = request["method"]?.GetValue<string>();
        if (method == null) return id == null ? null : Error(id, -32600, "Invalid request: no method");

        bool isNotification = id == null;
        var args = request["params"] as JsonObject;

        try
        {
            JsonNode? result = method switch
            {
                "initialize" => Initialize(args),
                "ping" => new JsonObject(),
                "tools/list" => new JsonObject { ["tools"] = Tools() },
                "tools/call" => await CallAsync(args, ct),
                "resources/list" => new JsonObject
                {
                    ["resources"] = new JsonArray(new JsonObject
                    {
                        ["uri"] = DesignGuideUri,
                        ["name"] = "MasterHub design guide",
                        ["description"] = "Hardware facts and rules for designing keys, lights and profiles.",
                        ["mimeType"] = "text/markdown"
                    })
                },
                "resources/read" => ReadResource(args),
                "prompts/list" => new JsonObject { ["prompts"] = new JsonArray() },
                _ when method.StartsWith("notifications/", StringComparison.Ordinal) => null,
                _ => throw new McpException(-32601, $"Method not found: {method}")
            };

            if (isNotification) return null;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result ?? new JsonObject() }.ToJsonString();
        }
        catch (McpException ex)
        {
            return isNotification ? null : Error(id, ex.Code, ex.Message);
        }
    }

    // ── protocol ────────────────────────────────────────────────────────────────────────────

    private static JsonObject Initialize(JsonObject? args)
    {
        string? asked = args?["protocolVersion"]?.GetValue<string>();
        string version = asked != null && SupportedVersions.Contains(asked) ? asked : SupportedVersions[0];

        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject(), ["resources"] = new JsonObject() },
            ["serverInfo"] = new JsonObject { ["name"] = "masterhub", ["version"] = typeof(McpServer).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" },
            ["instructions"] = "Read the resource masterhub://design-guide (or call design_guide) before designing keys. The MasterHub app must be running."
        };
    }

    private static JsonObject ReadResource(JsonObject? args)
    {
        string? uri = args?["uri"]?.GetValue<string>();
        if (uri != DesignGuideUri) throw new McpException(-32002, $"Resource not found: {uri}");

        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject { ["uri"] = DesignGuideUri, ["mimeType"] = "text/markdown", ["text"] = DesignGuide.Text })
        };
    }

    private static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    private sealed class McpException : Exception
    {
        public McpException(int code, string message) : base(message) => Code = code;
        public int Code { get; }
    }

    // ── tools ───────────────────────────────────────────────────────────────────────────────

    private static JsonObject Schema(string description, JsonObject properties, params string[] required) => new()
    {
        ["description"] = description,
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray())
        }
    };

    private static JsonObject Str(string description) => new() { ["type"] = "string", ["description"] = description };

    private static readonly Lazy<HashSet<string>> KnownTools = new(() => Tools().Select(t => t!["name"]!.GetValue<string>()).ToHashSet());
    private static HashSet<string> ToolNames => KnownTools.Value;

    private static JsonArray Tools()
    {
        static JsonObject Profile() => Str("Profile name or id. Leave out for the profile that is on the deck now.");   // a node can have one parent only: a new one each time
        var tools = new (string Name, JsonObject Def)[]
        {
            ("design_guide", Schema("The hardware facts and rules for designing the deck. Read this first.", new JsonObject())),
            ("list_modules", Schema("Which modules are connected (keys, faders, rollers, knobs, dial, base).", new JsonObject())),
            ("list_actions", Schema("The actions a key can run, with the limits an assistant has for each.",
                new JsonObject { ["search"] = Str("Only names that contain this text.") })),
            ("list_profiles", Schema("The profiles and which one is on the deck.", new JsonObject())),
            ("get_profile", Schema("One profile as JSON (credentials removed).", new JsonObject { ["profile"] = Profile() })),
            ("activate_profile", Schema("Put a profile on the deck.", new JsonObject { ["profile"] = Str("Profile name or id.") }, "profile")),
            ("set_control", Schema("Change one key. A change without 'action' only changes how the key looks and keeps what it does. All or nothing: a refused change says why.",
                new JsonObject
                {
                    ["control"] = Str("keys.0 … keys.14 (0 = top left), optionally ':hold' or ':double' for the hold / double-press binding."),
                    ["action"] = Str("A DeckActionType name from list_actions."),
                    ["parameter"] = Str("The action's parameter (a hotkey like 'Ctrl+Shift+M', a URL, a path)."),
                    ["title"] = Str("Label, up to 12 characters fit the LCD."),
                    ["subtitle"] = Str("Second line."),
                    ["background"] = Str("#rrggbb"),
                    ["text_color"] = Str("#rrggbb"),
                    ["page"] = new JsonObject { ["type"] = "integer", ["description"] = "Page of the profile, from 1." },
                    ["profile"] = Profile()
                }, "control")),
            ("clear_control", Schema("Empty a key (or remove its hold / double binding).",
                new JsonObject
                {
                    ["control"] = Str("keys.N, optionally ':hold' or ':double'."),
                    ["page"] = new JsonObject { ["type"] = "integer", ["description"] = "Page of the profile, from 1." },
                    ["profile"] = Profile()
                }, "control")),
            ("set_lights", Schema("Set the deck's lights for now (not saved; they stay until another profile is applied).",
                new JsonObject { ["mode"] = Str("profile, off, color or rainbow."), ["color"] = Str("#rrggbb, only with mode 'color'.") }, "mode"))
        };

        return new JsonArray(tools.Select(t =>
        {
            t.Def["name"] = t.Name;
            return (JsonNode)t.Def;
        }).ToArray());
    }

    private async Task<JsonObject> CallAsync(JsonObject? args, CancellationToken ct)
    {
        string name = args?["name"]?.GetValue<string>() ?? throw new McpException(-32602, "Invalid params: no tool name");
        var arguments = args["arguments"] as JsonObject ?? new JsonObject();

        if (!ToolNames.Contains(name)) throw new McpException(-32602, $"Unknown tool: {name}");

        try
        {
            string text = name switch
            {
                "design_guide" => DesignGuide.Text,
                "list_actions" => ListActions(Text(arguments, "search")),
                _ => await CallApiToolAsync(name, arguments, ct)
            };
            return ToolResult(text, isError: false);
        }
        catch (MasterHubNotRunningException ex)
        {
            return ToolResult(ex.Message + " Ask the user to start MasterHub, then try again.", isError: true);
        }
        catch (ApiException ex)
        {
            var lines = new List<string> { ex.Message };
            lines.AddRange(ex.Errors.Select(e => "- " + e));
            if (ex.Warnings.Count > 0) lines.AddRange(ex.Warnings.Select(w => "warning: " + w));
            return ToolResult(string.Join('\n', lines), isError: true);
        }
        catch (ToolInputException ex)
        {
            return ToolResult(ex.Message, isError: true);
        }
    }

    private async Task<string> CallApiToolAsync(string name, JsonObject a, CancellationToken ct)
    {
        using var client = _connect();   // port and token are read again for every call

        switch (name)
        {
            case "list_modules":
                return Json(await client.GetModulesAsync(ct));

            case "list_profiles":
            {
                var state = await client.GetStateAsync(ct);
                return Json(new
                {
                    active = state.ActiveProfileId,
                    profiles = state.Profiles.Select(p => new { id = p.Id, name = p.Name, isHome = p.IsDefault })
                });
            }

            case "get_profile":
                return (await client.GetProfileAsync((await ResolveAsync(client, Text(a, "profile"), ct)).Id, ct)).GetRawText();

            case "activate_profile":
            {
                var target = await ResolveAsync(client, Text(a, "profile"), ct, required: true);
                await client.ActivateProfileAsync(target.Id, ct);
                return $"\"{target.Name}\" is on the deck now.";
            }

            case "set_control":
            case "clear_control":
            {
                var target = await ResolveAsync(client, Text(a, "profile"), ct);
                bool clear = name == "clear_control";
                var change = new ControlChange
                {
                    Control = Text(a, "control") ?? "",
                    Clear = clear,
                    Action = clear ? null : Text(a, "action"),
                    Parameter = clear ? null : Text(a, "parameter"),
                    Title = clear ? null : Text(a, "title"),
                    Subtitle = clear ? null : Text(a, "subtitle"),
                    BackgroundColorHex = clear ? null : Text(a, "background"),
                    TextColorHex = clear ? null : Text(a, "text_color"),
                    Page = a["page"] is JsonValue v && v.TryGetValue<int>(out int page) ? page : null
                };

                var result = await client.SetControlsAsync(target.Id, new[] { change }, ct);
                string warnings = result.Warnings is { Count: > 0 } ? " Warnings: " + string.Join(" ", result.Warnings) : "";
                return $"Changed {change.Control} in \"{target.Name}\"." + warnings;
            }

            case "set_lights":
                await client.SetLightsAsync(Text(a, "mode") ?? "", Text(a, "color"), ct);
                return "The lights are set.";

            default:
                throw new McpException(-32602, $"Unknown tool: {name}");
        }
    }

    private static async Task<ApiProfile> ResolveAsync(ILocalApiClient client, string? query, CancellationToken ct, bool required = false)
    {
        var state = await client.GetStateAsync(ct);
        if (string.IsNullOrWhiteSpace(query))
        {
            if (required) throw new ToolInputException("Say which profile: its name or its id.");
            query = state.ActiveProfileId;
        }

        return ProfileResolver.Resolve(state.Profiles, query, out string? problem) ?? throw new ToolInputException(problem ?? "Unknown profile.");
    }

    private static string ListActions(string? search)
    {
        var holdOnly = new[] { DeckActionType.SystemShutdown, DeckActionType.SystemRestart, DeckActionType.SystemSleep, DeckActionType.CloseApplication, DeckActionType.SystemVirtualDesktopClose };

        var actions = Enum.GetValues<DeckActionType>()
            .Where(x => x is not (DeckActionType.None or DeckActionType.DefaultNumber or DeckActionType.StreamDeckPluginAction))
            .Select(x => x.ToString())
            .Where(n => string.IsNullOrWhiteSpace(search) || n.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new
            {
                name = n,
                limit = n == nameof(DeckActionType.HomeAssistantCallService) ? "not allowed for an assistant"
                    : holdOnly.Any(h => h.ToString() == n) ? "only on a hold (keys.N:hold)"
                    : n == nameof(DeckActionType.Hotkey) ? "closing shortcuts (Alt+F4, Ctrl+W …) only on a hold"
                    : n is nameof(DeckActionType.OpenApplication) or nameof(DeckActionType.OpenFile) ? "no scripts, installers or shells"
                    : null
            });

        return Json(actions);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private sealed class ToolInputException : Exception
    {
        public ToolInputException(string message) : base(message) { }
    }

    private static string? Text(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue<string>(out string? s) ? s : null;

    private static string Json(object value) => JsonSerializer.Serialize(value, ApiJson.Options);

    private static JsonObject ToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError
    };
}
