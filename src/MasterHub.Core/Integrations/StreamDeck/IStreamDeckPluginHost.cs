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
    event Action<int, string>? KeySettingsUpdated; // (keyIndex, settingsJson)
    event Action<string, string>? GlobalSettingsUpdated; // (pluginUuid, globalSettingsJson)
    event Action<string>? SwitchToProfileRequested; // (profileName)

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

    Task SendDialRotateAsync(int dialIndex, string pluginUuid, string actionUuid, int ticks, bool pressed, string? settingsJson = null);
    Task SendDialPressAsync(int dialIndex, string pluginUuid, string actionUuid, bool isDown, string? settingsJson = null);
    Task SendTouchTapAsync(int dialIndex, string pluginUuid, string actionUuid, float x, float y, bool hold, string? settingsJson = null);
    Task SendTitleParametersDidChangeAsync(int keyIndex, string pluginUuid, string actionUuid, string title, string? font = null, int? fontSize = null, string? fontColor = null);
    Task SendDeviceDidConnectAsync(string deviceId, string deviceName, int type, int cols, int rows);
    Task SendDeviceDidDisconnectAsync(string deviceId);
    Task SendSystemDidWakeUpAsync();
    Task SendApplicationDidLaunchAsync(string application);
    Task SendApplicationDidTerminateAsync(string application);

    StreamDeckActionManifest? GetActionManifest(string pluginUuid, string actionUuid);
    StreamDeckInstalledPlugin? GetPlugin(string pluginUuid);

    string? GetPropertyInspectorPath(string pluginUuid, string? actionUuid = null);
    bool OpenPropertyInspector(int keyIndex, string pluginUuid, string actionUuid, string? currentSettingsJson = null);
    bool OpenGlobalPropertyInspector(string pluginUuid);
    void OpenPluginFolder(string pluginUuid);
    Task UpdateKeySettingsAsync(int keyIndex, string pluginUuid, string actionUuid, string settingsJson);
    Task UpdateGlobalSettingsAsync(string pluginUuid, string globalSettingsJson);
    string? GetGlobalSettings(string pluginUuid);
    string? GetKeySettings(int keyIndex);
}
