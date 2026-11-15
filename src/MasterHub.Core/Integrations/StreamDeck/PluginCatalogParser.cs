using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>
/// Reads the plugin index. Two shapes are understood:
/// <list type="bullet">
/// <item>the public OpenAction plugin registry: one JSON object whose keys are plugin ids and whose values hold
/// <c>name</c>, <c>author</c>, <c>repository</c>, <c>description</c> and optionally <c>download_url</c> and <c>flags</c>;</item>
/// <item>our own earlier shape <c>{ "plugins": [ … ] }</c>, which is also what the on-disk cache holds.</item>
/// </list>
/// The index is remote input: ids are checked before they are used in a URL, entries without anything to download are dropped.
/// </summary>
public static class PluginCatalogParser
{
    /// <summary>Where the registry keeps one icon per plugin id.</summary>
    public const string RegistryIconBase = "https://openactionapi.github.io/plugins/icons/";

    public static List<PluginCatalogEntry> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return new();

        if (root.TryGetProperty("plugins", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
        {
            var file = JsonSerializer.Deserialize<PluginCatalogFile>(json);
            return file?.Plugins ?? new();
        }

        var list = new List<PluginCatalogEntry>();
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;
            var entry = FromRegistryEntry(prop.Name, prop.Value);
            if (entry != null) list.Add(entry);
        }
        return list;
    }

    /// <summary>True for ids made of letters, digits, '.', '-' and '_' only: safe in a URL path and in a file name.</summary>
    public static bool IsSafeId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= 120 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    private static PluginCatalogEntry? FromRegistryEntry(string id, JsonElement e)
    {
        string name = Str(e, "name");
        string repository = Str(e, "repository");
        string download = Str(e, "download_url");
        if (name.Length == 0 || (repository.Length == 0 && download.Length == 0)) return null;

        return new PluginCatalogEntry
        {
            Id = id,
            Name = name,
            Author = Str(e, "author"),
            Description = Str(e, "description"),
            Repository = repository,
            DownloadUrl = download.Length > 0 ? download : null,
            Icon = IsSafeId(id) ? RegistryIconBase + id + ".png" : "",
            AiAssisted = e.TryGetProperty("flags", out var flags) && flags.ValueKind == JsonValueKind.Object
                         && flags.TryGetProperty("ai_assisted", out var ai) && ai.ValueKind == JsonValueKind.True
        };
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
}
