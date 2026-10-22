using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MasterHub.Core.Integrations.StreamDeck;

public class PluginCatalogFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("plugins")]
    public List<PluginCatalogEntry> Plugins { get; set; } = new();
}

public class PluginCatalogEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("repository")]
    public string Repository { get; set; } = "";

    [JsonPropertyName("icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("download_url")]
    public string? DownloadUrl { get; set; }

    [JsonPropertyName("aiAssisted")]
    public bool AiAssisted { get; set; }

    [JsonPropertyName("popular")]
    public int? Popular { get; set; }

    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    [JsonPropertyName("stars")]
    public int? Stars { get; set; }

    [JsonPropertyName("updated")]
    public string? Updated { get; set; }

    [JsonPropertyName("systems")]
    public List<string> Systems { get; set; } = new();

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("dial")]
    public bool Dial { get; set; }
}
