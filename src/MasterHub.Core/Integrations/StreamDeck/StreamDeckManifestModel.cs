using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MasterHub.Core.Integrations.StreamDeck;

public class StreamDeckManifest
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("Author")]
    public string Author { get; set; } = "";

    [JsonPropertyName("Version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("Icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("URL")]
    public string? URL { get; set; }

    [JsonPropertyName("Category")]
    public string Category { get; set; } = "Custom";

    [JsonPropertyName("CategoryIcon")]
    public string CategoryIcon { get; set; } = "";

    [JsonPropertyName("CodePath")]
    public string CodePath { get; set; } = "";

    [JsonPropertyName("CodePathWin")]
    public string? CodePathWin { get; set; }

    [JsonPropertyName("CodePathMac")]
    public string? CodePathMac { get; set; }

    [JsonPropertyName("PropertyInspectorPath")]
    public string? PropertyInspectorPath { get; set; }

    [JsonPropertyName("SDKVersion")]
    public int SDKVersion { get; set; } = 2;

    [JsonPropertyName("UUID")]
    public string? UUID { get; set; }

    [JsonPropertyName("DefaultWindowSize")]
    public List<int>? DefaultWindowSize { get; set; }

    [JsonPropertyName("ApplicationsToMonitor")]
    public Dictionary<string, List<string>>? ApplicationsToMonitor { get; set; }

    [JsonPropertyName("Actions")]
    public List<StreamDeckActionManifest> Actions { get; set; } = new();

    [JsonPropertyName("Nodejs")]
    public StreamDeckNodejs? Nodejs { get; set; }
}

public class StreamDeckNodejs
{
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "20";
}

public class StreamDeckActionManifest
{
    [JsonPropertyName("UUID")]
    public string UUID { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Icon")]
    public string Icon { get; set; } = "";

    [JsonPropertyName("Tooltip")]
    public string? Tooltip { get; set; }

    [JsonPropertyName("PropertyInspectorPath")]
    public string? PropertyInspectorPath { get; set; }

    [JsonPropertyName("UserTitleEnabled")]
    public bool UserTitleEnabled { get; set; } = true;

    [JsonPropertyName("Controllers")]
    public List<string>? Controllers { get; set; }

    [JsonPropertyName("Encoder")]
    public StreamDeckEncoderManifest? Encoder { get; set; }

    [JsonPropertyName("SupportedInMultiActions")]
    public bool SupportedInMultiActions { get; set; } = true;

    [JsonPropertyName("DisableAutomaticStates")]
    public bool DisableAutomaticStates { get; set; }

    [JsonPropertyName("VisibleInActionsList")]
    public bool VisibleInActionsList { get; set; } = true;

    [JsonPropertyName("States")]
    public List<StreamDeckActionState> States { get; set; } = new();

    public bool SupportsKeypad => Controllers == null || Controllers.Count == 0 || Controllers.Exists(c => c.Equals("Keypad", StringComparison.OrdinalIgnoreCase));
    public bool SupportsEncoder => Controllers != null && Controllers.Exists(c => c.Equals("Encoder", StringComparison.OrdinalIgnoreCase));
    public string ControllersDisplay => SupportsEncoder ? (SupportsKeypad ? "Klawisze + Pokrętła" : "Pokrętła (Encoder)") : "Klawisze";
}

public class StreamDeckEncoderManifest
{
    [JsonPropertyName("layout")]
    public string? Layout { get; set; }

    [JsonPropertyName("StackColor")]
    public string? StackColor { get; set; }

    [JsonPropertyName("TriggerDescription")]
    public StreamDeckTriggerDescription? TriggerDescription { get; set; }
}

public class StreamDeckTriggerDescription
{
    [JsonPropertyName("Rotate")]
    public string? Rotate { get; set; }

    [JsonPropertyName("Push")]
    public string? Push { get; set; }

    [JsonPropertyName("Touch")]
    public string? Touch { get; set; }

    [JsonPropertyName("LongPush")]
    public string? LongPush { get; set; }
}

public class StreamDeckActionState
{
    [JsonPropertyName("Image")]
    public string Image { get; set; } = "";

    [JsonPropertyName("Title")]
    public string? Title { get; set; }

    [JsonPropertyName("ShowTitle")]
    public bool ShowTitle { get; set; } = true;

    [JsonPropertyName("TitleColor")]
    public string? TitleColor { get; set; }

    [JsonPropertyName("FontSize")]
    public int? FontSize { get; set; }
}

public class StreamDeckInstalledPlugin
{
    public string PluginUuid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string? IconPath { get; set; }
    public bool IsNodeJs { get; set; }
    public bool IsHtml { get; set; }
    public string? HtmlEntryPath { get; set; }
    public bool IsRunning { get; set; }
    public bool IsEnabled { get; set; } = true;
    public StreamDeckManifest Manifest { get; set; } = new();

    public string? Url => Manifest.URL;
    public bool HasDials => Manifest.Actions.Exists(a => a.SupportsEncoder);
    public string StatusDisplay => IsRunning ? "🟢 Uruchomiono" : "⚪ Gotowy";
    public string ToggleButtonText => IsRunning ? "⏹️ Zatrzymaj" : "▶️ Uruchom";
    public string ActionsCountDisplay => $"{Manifest.Actions.Count} akcji" + (HasDials ? " (w tym pokrętła)" : "");
    public bool HasPropertyInspector => !string.IsNullOrEmpty(Manifest.PropertyInspectorPath) || Manifest.Actions.Exists(a => !string.IsNullOrEmpty(a.PropertyInspectorPath));
}
