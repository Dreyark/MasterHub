using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>
/// Interfejs hosta wtyczek Elgato Stream Deck (.streamDeckPlugin) w MasterHub.
/// Zarządza cyklem życia procesów wtyczek, serwerem WebSocket i buforami graficznymi.
/// </summary>
public interface IStreamDeckPluginHost : IDisposable
{
    int Port { get; }
    bool IsRunning { get; }
    IReadOnlyList<StreamDeckInstalledPlugin> InstalledPlugins { get; }

    event Action<int, byte[]>? KeyImageUpdated; // (keyIndex, jpegBytes72x72)
    event Action<int, string>? KeyTitleUpdated; // (keyIndex, title)
    event Action<int, int>? KeyStateUpdated; // (keyIndex, state)
    event Action<int>? KeyAlertRequested; // (keyIndex)
    event Action<int>? KeyOkRequested; // (keyIndex)
    event Action<string>? PluginInstalled; // (pluginUuid)
    event Action<string, string>? PluginLogReceived; // (pluginUuid, message)

    void RefreshPlugins();
    Task<StreamDeckInstalledPlugin?> InstallPluginPackageAsync(string packageFilePath);
    Task<bool> UninstallPluginAsync(string pluginUuid);
    Task StartAsync();
    Task StopAsync();
    Task StartPluginAsync(string pluginUuid);
    Task StopPluginAsync(string pluginUuid);

    Task SendWillAppearAsync(int keyIndex, string pluginUuid, string actionUuid, string? settingsJson = null);
    Task SendWillDisappearAsync(int keyIndex, string pluginUuid, string actionUuid);
    Task SendKeyDownAsync(int keyIndex, string pluginUuid, string actionUuid, string? settingsJson = null);
    Task SendKeyUpAsync(int keyIndex, string pluginUuid, string actionUuid, string? settingsJson = null);

    StreamDeckActionManifest? GetActionManifest(string pluginUuid, string actionUuid);
    StreamDeckInstalledPlugin? GetPlugin(string pluginUuid);
}
