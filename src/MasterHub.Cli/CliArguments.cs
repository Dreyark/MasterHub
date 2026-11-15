using System;
using System.Collections.Generic;

namespace MasterHub.Cli;

/// <summary>
/// The arguments of one command line, split into the words after the command, <c>--name value</c> options and <c>--flag</c> switches.
/// Each command says which options take a value, so a value that starts with a dash (<c>--label -5</c>) is still a value.
/// </summary>
internal sealed class CliArguments
{
    public List<string> Positionals { get; } = new();
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Flags { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Has(string name) => Flags.Contains(name) || Values.ContainsKey(name);

    public string? Value(string name) => Values.TryGetValue(name, out var v) ? v : null;

    /// <summary>Parses <paramref name="args"/>; <paramref name="error"/> says what is wrong when the result is <c>null</c>.</summary>
    public static CliArguments? Parse(IEnumerable<string> args, ISet<string> valueOptions, ISet<string> flagOptions, out string? error)
    {
        error = null;
        var result = new CliArguments();
        var queue = new Queue<string>(args);

        while (queue.Count > 0)
        {
            string arg = queue.Dequeue();
            if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length == 2)
            {
                result.Positionals.Add(arg);
                continue;
            }

            string name = arg[2..];
            string? inline = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                inline = name[(equals + 1)..];
                name = name[..equals];
            }

            if (valueOptions.Contains(name))
            {
                if (inline != null) result.Values[name] = inline;
                else if (queue.Count > 0) result.Values[name] = queue.Dequeue();
                else
                {
                    error = $"--{name} needs a value.";
                    return null;
                }
            }
            else if (flagOptions.Contains(name))
            {
                if (inline != null)
                {
                    error = $"--{name} does not take a value.";
                    return null;
                }
                result.Flags.Add(name);
            }
            else
            {
                error = $"Unknown option --{name}.";
                return null;
            }
        }

        return result;
    }
}
