using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Integrations.StreamDeck;

namespace MasterHub.Avalonia.ViewModels.Pages;

public partial class PluginSettingsViewModel : ObservableObject
{
    private readonly IStreamDeckPluginHost? _streamDeckHost;

    public StreamDeckPluginItemViewModel PluginItem { get; }
    public StreamDeckInstalledPlugin Plugin => PluginItem.Plugin;
    public string PluginName => Plugin.Name;
    public string Version => Plugin.Version;
    public string Author => Plugin.Author;
    public string Description => Plugin.Description;
    public string PluginUuid => Plugin.PluginUuid;
    public string FolderPath => Plugin.FolderPath;
    public string? Url => Plugin.Url;
    public bool HasUrl => !string.IsNullOrEmpty(Plugin.Url);
    public bool HasDials => Plugin.HasDials;
    public Bitmap? PluginBitmap => PluginItem.PluginBitmap;
    public bool HasPropertyInspector => Plugin.HasPropertyInspector;
    public ObservableCollection<StreamDeckActionManifest> Actions { get; } = new();

    [ObservableProperty]
    private string _globalSettingsJson = "{}";

    [ObservableProperty]
    private string _statusMessage = "";

    public Action? RequestClose { get; set; }

    public PluginSettingsViewModel(StreamDeckPluginItemViewModel pluginItem, IStreamDeckPluginHost? host)
    {
        PluginItem = pluginItem;
        _streamDeckHost = host;

        string? existing = host?.GetGlobalSettings(pluginItem.PluginUuid);
        _globalSettingsJson = !string.IsNullOrWhiteSpace(existing) ? FormatJson(existing) : "{}";

        if (Plugin.Manifest?.Actions != null)
        {
            foreach (var act in Plugin.Manifest.Actions)
            {
                Actions.Add(act);
            }
        }
    }

    [RelayCommand]
    public async Task SaveGlobalSettingsAsync()
    {
        if (string.IsNullOrWhiteSpace(GlobalSettingsJson))
        {
            GlobalSettingsJson = "{}";
        }

        try
        {
            using var doc = JsonDocument.Parse(GlobalSettingsJson);
            string formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            GlobalSettingsJson = formatted;

            if (_streamDeckHost != null)
            {
                await _streamDeckHost.UpdateGlobalSettingsAsync(PluginUuid, formatted);
                StatusMessage = "Ustawienia wtyczki zostały pomyślnie zapisane.";
            }
        }
        catch (JsonException ex)
        {
            StatusMessage = $"Błąd JSON: {ex.Message}";
        }
    }

    [RelayCommand]
    public void FormatJsonCommand()
    {
        try
        {
            using var doc = JsonDocument.Parse(GlobalSettingsJson);
            GlobalSettingsJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
            StatusMessage = "Sformatowano poprawny JSON.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Błąd formatowania: {ex.Message}";
        }
    }

    [RelayCommand]
    public void OpenGlobalPropertyInspector()
    {
        _streamDeckHost?.OpenGlobalPropertyInspector(PluginUuid);
    }

    [RelayCommand]
    public void OpenActionPropertyInspector(string? actionUuid)
    {
        if (!string.IsNullOrEmpty(actionUuid))
        {
            _streamDeckHost?.OpenPropertyInspector(-1, PluginUuid, actionUuid, null);
        }
    }

    [RelayCommand]
    public void OpenUrl()
    {
        if (!string.IsNullOrEmpty(Url))
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = Url, UseShellExecute = true });
            }
            catch { }
        }
    }

    [RelayCommand]
    public void OpenFolder()
    {
        _streamDeckHost?.OpenPluginFolder(PluginUuid);
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }

    private static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}
