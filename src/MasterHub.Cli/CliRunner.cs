using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MasterHub.Core.Api;
using MasterHub.Core.Api.Client;

namespace MasterHub.Cli;

/// <summary>
/// The command line of MasterHub (T-24): <c>status</c>, <c>profiles</c>, <c>profile</c>, <c>key</c>, <c>lights</c>, <c>doctor</c>. It only talks to the
/// running app over its local API; it never touches the deck or the profile files itself. Everything it prints goes to the writers it is
/// given, and it returns the exit code (0 = it worked, 1 = it did not, with the reason on the error writer), so tests run it in-process.
/// </summary>
public sealed partial class CliRunner
{
    public const int Ok = 0;
    public const int Failed = 1;

    private static readonly HashSet<string> NoOptions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> JsonFlag = new(StringComparer.OrdinalIgnoreCase) { "json" };

    private static readonly HashSet<string> KeyValueOptions = new(StringComparer.OrdinalIgnoreCase)
        { "label", "subtitle", "color", "text-color", "action", "param", "page", "profile", "icon" };
    private static readonly HashSet<string> KeyFlags = new(StringComparer.OrdinalIgnoreCase) { "clear" };

    [GeneratedRegex(@"^#[0-9a-fA-F]{6}$")]
    private static partial Regex ColorRegex();

    private readonly Func<ILocalApiClient> _connect;
    private readonly TextReader _input;
    private readonly Func<ILocalApiClient> _connectAssistant;

    /// <param name="connect">Opens the connection to the app (reads its port and token). The default reads the files in <c>%APPDATA%\MasterHub</c>.</param>
    /// <param name="input">What <c>--claude-hook</c> reads (the hook's JSON). Default: the console input.</param>
    /// <param name="connectAssistant">How the MCP server opens its connection (it marks its requests as an assistant's). Tests give a fake.</param>
    public CliRunner(Func<ILocalApiClient>? connect = null, TextReader? input = null, Func<ILocalApiClient>? connectAssistant = null)
    {
        _connectAssistant = connectAssistant ?? ConnectAsAssistant;
        _input = input ?? Console.In;
        _connect = connect ?? (() => new LocalApiClient(ApiConnection.Read()));
    }

    /// <summary>Runs one command line. Never throws for a problem the user can fix: it prints the reason and returns <see cref="Failed"/>.</summary>
    public async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken ct = default)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h" or "/?")
        {
            (args.Length == 0 ? error : output).Write(HelpText);
            return args.Length == 0 ? Failed : Ok;
        }

        if (args[0] is "--version" or "version")
        {
            output.WriteLine($"masterhub-cli {typeof(CliRunner).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"} (API {ApiVersion.Current})");
            return Ok;
        }

        if (args[0] == "--claude-hook") return await ClaudeHookAsync(_input, ct);
        if (args[0] == "--mcp") return await McpAsync(output, ct);

        string command = args[0].ToLowerInvariant();
        var rest = args.Skip(1).ToList();
        if (rest.Contains("--help") || rest.Contains("-h"))
        {
            output.Write(HelpText);
            return Ok;
        }

        try
        {
            return command switch
            {
                "status" => await StatusAsync(rest, output, error, ct),
                "profiles" => await ProfilesAsync(rest, output, error, ct),
                "profile" => await ProfileAsync(rest, output, error, ct),
                "key" => await KeyAsync(rest, output, error, ct),
                "lights" => await LightsAsync(rest, output, error, ct),
                "doctor" => await DoctorAsync(rest, output, ct),
                _ => Fail(error, $"Unknown command \"{args[0]}\". Run \"masterhub-cli help\".")
            };
        }
        catch (MasterHubNotRunningException ex)
        {
            return Fail(error, ex.Message);
        }
        catch (ApiException ex)
        {
            error.WriteLine("error: " + ex.Message);
            foreach (string reason in ex.Errors) error.WriteLine("  - " + reason);
            return Failed;
        }
        catch (OperationCanceledException)
        {
            return Fail(error, "Cancelled.");
        }
    }

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine("error: " + message);
        return Failed;
    }

    private static CliArguments? Parse(List<string> rest, ISet<string> values, ISet<string> flags, TextWriter error)
    {
        var parsed = CliArguments.Parse(rest, values, flags, out string? problem);
        if (parsed == null) error.WriteLine("error: " + problem);
        return parsed;
    }

    // ── status ──────────────────────────────────────────────────────────────────────────────

    private async Task<int> StatusAsync(List<string> rest, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var args = Parse(rest, NoOptions, JsonFlag, error);
        if (args == null) return Failed;
        if (args.Positionals.Count > 0) return Fail(error, "\"status\" takes no arguments.");

        using var client = _connect();
        var version = await client.GetVersionAsync(ct);
        var state = await client.GetStateAsync(ct);
        var modules = await client.GetModulesAsync(ct);

        if (args.Has("json"))
        {
            output.WriteLine(JsonSerializer.Serialize(new { version, state, modules }, ApiJson.Options));
            return Ok;
        }

        var active = state.Profiles.FirstOrDefault(p => p.Id == state.ActiveProfileId);
        var kinds = modules.ConnectedKinds();
        output.WriteLine($"MasterHub {version.Version} (API {version.Api})");
        output.WriteLine($"Active profile: {(active == null ? "(a preview is on the deck)" : $"{active.Name} ({active.Id})")}");
        output.WriteLine($"Auto-switch:    {(state.AutoSwitch == true ? "on" : "off")}");
        if (state.Foreground is { Process.Length: > 0 } front)
            output.WriteLine($"In front:       {front.Process}{(string.IsNullOrEmpty(front.Title) ? "" : " – " + front.Title)}");
        output.WriteLine($"Deck:           {(kinds.Count == 0 ? "no module connected" : string.Join(", ", kinds))}");
        return Ok;
    }

    // ── profiles / profile ──────────────────────────────────────────────────────────────────

    private async Task<int> ProfilesAsync(List<string> rest, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var args = Parse(rest, NoOptions, JsonFlag, error);
        if (args == null) return Failed;
        if (args.Positionals.Count > 0) return Fail(error, "\"profiles\" takes no arguments.");

        using var client = _connect();
        var state = await client.GetStateAsync(ct);

        if (args.Has("json"))
        {
            output.WriteLine(JsonSerializer.Serialize(new { activeProfileId = state.ActiveProfileId, profiles = state.Profiles }, ApiJson.Options));
            return Ok;
        }

        // * = the profile on the deck now, ^ = Home (the one the app starts with)
        foreach (var p in state.Profiles)
            output.WriteLine($"{(p.Id == state.ActiveProfileId ? "*" : " ")}{(p.IsDefault ? "^" : " ")} {p.Name}  ({p.Id})");
        return Ok;
    }

    private async Task<int> ProfileAsync(List<string> rest, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var args = Parse(rest, NoOptions, NoOptions, error);
        if (args == null) return Failed;
        if (args.Positionals.Count != 1) return Fail(error, "Usage: masterhub-cli profile <name or id>");

        using var client = _connect();
        var state = await client.GetStateAsync(ct);
        var profile = ProfileResolver.Resolve(state.Profiles, args.Positionals[0], out string? problem);
        if (profile == null) return Fail(error, problem ?? "Unknown profile.");

        await client.ActivateProfileAsync(profile.Id, ct);
        output.WriteLine($"Profile \"{profile.Name}\" is on the deck.");
        return Ok;
    }

    // ── key ─────────────────────────────────────────────────────────────────────────────────

    private async Task<int> KeyAsync(List<string> rest, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var args = Parse(rest, KeyValueOptions, KeyFlags, error);
        if (args == null) return Failed;

        if (args.Positionals.Count != 1 || !int.TryParse(args.Positionals[0], out int number) || number is < 1 or > ProfileValidator.KeyCount)
            return Fail(error, $"Usage: masterhub-cli key <1-{ProfileValidator.KeyCount}> [--label TEXT] [--subtitle TEXT] [--color #rrggbb] [--text-color #rrggbb] "
                               + "[--action NAME [--param TEXT]] [--clear] [--page N] [--profile NAME]. Keys are numbered left to right, top to bottom.");

        if (args.Has("icon"))
            return Fail(error, "--icon is not available: a key's icon follows its action (use --action), or set a picture in the Studio.");

        foreach (string colorOption in new[] { "color", "text-color" })
            if (args.Value(colorOption) is { } color && !ColorRegex().IsMatch(color))
                return Fail(error, $"--{colorOption} must be a #rrggbb colour, not \"{color}\".");

        int? page = null;
        if (args.Value("page") is { } pageText)
        {
            if (!int.TryParse(pageText, out int p) || p < 1) return Fail(error, $"--page must be a number from 1, not \"{pageText}\".");
            page = p;
        }

        bool clear = args.Has("clear");
        bool any = args.Has("label") || args.Has("subtitle") || args.Has("color") || args.Has("text-color") || args.Has("action");
        if (clear && any) return Fail(error, "--clear cannot be combined with other changes.");
        if (!clear && !any) return Fail(error, "Nothing to change: give --label, --subtitle, --color, --text-color, --action or --clear.");
        if (args.Has("param") && !args.Has("action")) return Fail(error, "--param belongs to --action.");

        using var client = _connect();
        var state = await client.GetStateAsync(ct);

        ApiProfile? target;
        if (args.Value("profile") is { } profileText)
        {
            target = ProfileResolver.Resolve(state.Profiles, profileText, out string? problem);
            if (target == null) return Fail(error, problem ?? "Unknown profile.");
        }
        else
        {
            target = state.Profiles.FirstOrDefault(p => p.Id == state.ActiveProfileId);
            if (target == null) return Fail(error, "No profile is active (a preview is on the deck). Name one with --profile.");
        }

        var change = new ControlChange
        {
            Control = $"keys.{number - 1}",
            Clear = clear,
            Title = args.Value("label"),
            Subtitle = args.Value("subtitle"),
            BackgroundColorHex = args.Value("color"),
            TextColorHex = args.Value("text-color"),
            Action = args.Value("action"),
            Parameter = args.Value("param"),
            Page = page
        };

        var result = await client.SetControlsAsync(target.Id, new[] { change }, ct);
        foreach (string warning in result.Warnings ?? new()) error.WriteLine("warning: " + warning);
        output.WriteLine($"Key {number}{(page is > 1 ? $" on page {page}" : "")} of \"{target.Name}\" {(clear ? "is empty now" : "was updated")}.");
        return Ok;
    }

    // ── lights ──────────────────────────────────────────────────────────────────────────────

    private async Task<int> LightsAsync(List<string> rest, TextWriter output, TextWriter error, CancellationToken ct)
    {
        var args = Parse(rest, NoOptions, NoOptions, error);
        if (args == null) return Failed;

        const string usage = "Usage: masterhub-cli lights <rainbow | color #rrggbb | profile | off>";
        if (args.Positionals.Count == 0) return Fail(error, usage);

        string mode = args.Positionals[0].ToLowerInvariant();
        string? color = args.Positionals.Count > 1 ? args.Positionals[1] : null;

        if (mode is not ("rainbow" or "color" or "profile" or "off")) return Fail(error, $"Unknown lights mode \"{args.Positionals[0]}\". {usage}");
        if (mode == "color" && color == null) return Fail(error, "\"lights color\" needs a colour: masterhub-cli lights color #ff8800");
        if (mode != "color" && color != null) return Fail(error, $"\"lights {mode}\" takes no colour.");
        if (color != null && !ColorRegex().IsMatch(color)) return Fail(error, $"The colour must be #rrggbb, not \"{color}\".");
        if (args.Positionals.Count > 2) return Fail(error, usage);

        using var client = _connect();
        await client.SetLightsAsync(mode, color, ct);
        output.WriteLine(mode switch
        {
            "profile" => "The lights are the profile's own again.",
            "off" => "The lights are off (until another profile is applied or \"lights profile\").",
            "color" => $"The lights are {color} (until another profile is applied or \"lights profile\").",
            _ => "The lights are a rainbow (until another profile is applied or \"lights profile\")."
        });
        return Ok;
    }

    // ── doctor ──────────────────────────────────────────────────────────────────────────────

    private async Task<int> DoctorAsync(List<string> rest, TextWriter output, CancellationToken ct)
    {
        var checks = await Doctor.RunAsync(_connect, ct);
        foreach (var check in checks)
        {
            string mark = check.Status switch { CheckStatus.Ok => "[ok]  ", CheckStatus.Warning => "[warn]", _ => "[fail]" };
            output.WriteLine($"{mark} {check.Title}{(string.IsNullOrEmpty(check.Detail) ? "" : " – " + check.Detail)}");
        }

        return Doctor.Passed(checks) ? Ok : Failed;
    }

    public const string HelpText = """
        masterhub-cli – drives the running MasterHub app over its local API.

        Usage:
          masterhub-cli status [--json]                  version, active profile, deck
          masterhub-cli profiles [--json]                list profiles  (* active, ^ Home)
          masterhub-cli profile <name or id>             put a profile on the deck
          masterhub-cli key <1-15> [options]             change a key of the active profile
              --label TEXT  --subtitle TEXT  --color #rrggbb  --text-color #rrggbb
              --action NAME [--param TEXT]   --clear   --page N   --profile NAME
          masterhub-cli lights <rainbow | color #rrggbb | profile | off>
          masterhub-cli doctor                           check that the app answers
          masterhub-cli --version
          masterhub-cli --mcp                            MCP server for AI assistants (stdio); MASTERHUB_MCP_MORE_POWER=1 lifts two limits
          masterhub-cli --claude-hook                    for Claude Code hooks (reads the hook JSON on its input, prints nothing)

        Keys are numbered left to right, top to bottom (1 = top left). A key without --action keeps what it does and only
        changes how it looks. Lights stay until another profile is applied or "lights profile".
        Exit code: 0 = done, 1 = not done (the reason is printed). The app must be running; the CLI never touches the deck itself.

        """;
}
