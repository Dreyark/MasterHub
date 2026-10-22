using System;
using System.Collections.ObjectModel;
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
        _globalSettingsJson = !string.IsNullOrWhiteSpace(existing) ? existing : "{}";

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
        if (_streamDeckHost != null)
        {
            await _streamDeckHost.UpdateGlobalSettingsAsync(PluginUuid, GlobalSettingsJson);
            StatusMessage = "Ustawienia wtyczki zostały pomyślnie zapisane.";
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
    public void OpenFolder()
    {
        _streamDeckHost?.OpenPluginFolder(PluginUuid);
    }

    [RelayCommand]
    public void Close()
    {
        RequestClose?.Invoke();
    }
}
