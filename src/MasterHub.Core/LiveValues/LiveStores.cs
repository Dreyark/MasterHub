using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MasterHub.Core.Security;

namespace MasterHub.Core.LiveValues;

/// <summary>
/// Commands a live-value key may run, approved <b>per exact command line</b> (a changed character needs a new approval).
/// The list is a JSON file; deleting a line revokes the approval.
/// </summary>
public sealed class LiveCommandApprovalStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public LiveCommandApprovalStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "live-commands.json");
    }

    public IReadOnlyList<string> All()
    {
        lock (_lock) { return Load(); }
    }

    public bool IsApproved(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return false;
        lock (_lock) { return Load().Contains(commandLine, StringComparer.Ordinal); }
    }

    public void Approve(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return;
        lock (_lock)
        {
            var list = Load();
            if (!list.Contains(commandLine, StringComparer.Ordinal))
            {
                list.Add(commandLine);
                Save(list);
            }
        }
    }

    public void Revoke(string commandLine)
    {
        lock (_lock)
        {
            var list = Load();
            if (list.RemoveAll(c => string.Equals(c, commandLine, StringComparison.Ordinal)) > 0) Save(list);
        }
    }

    private List<string> Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? new() : new();
        }
        catch
        {
            return new(); // unreadable file: nothing is approved
        }
    }

    private void Save(List<string> list)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _path, overwrite: true);
    }
}

/// <summary>Bearer tokens of live-value keys, DPAPI-protected, by name. A profile keeps only the name, so neither the profile nor an export carries a token.</summary>
public sealed class LiveTokenStore
{
    private readonly string _path;
    private readonly ISecretProtectionService _secrets;
    private readonly object _lock = new();

    public LiveTokenStore(ISecretProtectionService secrets, string? path = null)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub", "live-tokens.json");
    }

    public string? Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        lock (_lock)
        {
            return Load().TryGetValue(name, out var protectedValue) ? _secrets.Unprotect(protectedValue) : null;
        }
    }

    public void Set(string name, string token)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        lock (_lock)
        {
            var map = Load();
            if (string.IsNullOrEmpty(token)) map.Remove(name);
            else map[name] = _secrets.Protect(token) ?? "";
            Save(map);
        }
    }

    public IReadOnlyCollection<string> Names()
    {
        lock (_lock) { return Load().Keys.ToList(); }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_path)) ?? new()
                : new();
        }
        catch
        {
            return new();
        }
    }

    private void Save(Dictionary<string, string> map)
    {
        string? dir = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string tmp = $"{_path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(map));
        File.Move(tmp, _path, overwrite: true);
    }
}
