using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.SystemControl;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Builds the tray menu entries that act on the app (T-34 #19): a profile list with the current one ticked, the autostart switch and
/// a shortcut to the data folder. The entries read their state when the menu is about to open, so they are never stale.
/// Kept apart from the window code so it can be tested without a window.
/// </summary>
public sealed class TrayMenuBuilder
{
    private readonly IProfileService? _profiles;
    private readonly Action<string>? _activateProfile;
    private readonly IStartupService? _startup;
    private readonly Action<string> _openFolder;
    private readonly Func<string, string, string> _text;

    public TrayMenuBuilder(IProfileService? profiles, Action<string>? activateProfile, IStartupService? startup,
        Action<string>? openFolder = null, Func<string, string, string>? text = null)
    {
        _profiles = profiles;
        _activateProfile = activateProfile;
        _startup = startup;
        _openFolder = openFolder ?? OpenInExplorer;
        _text = text ?? ((key, fallback) => LocalizationService.Instance.GetString(key, fallback));
    }

    /// <summary>The folder with profiles, settings and media.</summary>
    public static string DataFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub");

    /// <summary>Adds the profile submenu, the autostart switch and the data folder entry to <paramref name="menu"/>.</summary>
    public void AddTo(NativeMenu menu)
    {
        if (_profiles != null && _activateProfile != null) menu.Add(BuildProfilesItem());
        if (_startup != null) menu.Add(BuildAutostartItem());
        menu.Add(BuildFolderItem());
    }

    // ── profiles ────────────────────────────────────────────────────────────────────────────

    private NativeMenuItem BuildProfilesItem()
    {
        var root = new NativeMenuItem(_text("Tray_Profiles", "Profiles")) { Menu = new NativeMenu() };
        Refill(root.Menu!);
        root.Menu!.NeedsUpdate += (_, _) => Refill(root.Menu!);
        return root;
    }

    /// <summary>Rebuilds the profile entries from the current list; also called by tests.</summary>
    internal void Refill(NativeMenu submenu)
    {
        submenu.Items.Clear();

        IReadOnlyList<ProfileModel> all;
        string activeId;
        try
        {
            all = _profiles!.GetAllProfiles();
            activeId = _profiles.GetActiveProfile().Id;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            all = Array.Empty<ProfileModel>();   // the profile store is busy or unreadable: show an empty list, never break the tray
            activeId = "";
        }

        foreach (var profile in all)
        {
            string id = profile.Id;
            var item = new NativeMenuItem(profile.Name) { ToggleType = MenuItemToggleType.Radio, IsChecked = id == activeId };
            item.Command = new RelayCommand(() => _activateProfile!(id));
            submenu.Items.Add(item);
        }
    }

    // ── autostart ───────────────────────────────────────────────────────────────────────────

    private NativeMenuItem BuildAutostartItem()
    {
        string text = _text("Tray_Autostart", "Start with Windows");
        var item = new NativeMenuItem(text) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = SafeIsEnabled() };
        item.Command = new RelayCommand(() =>
        {
            bool now = !SafeIsEnabled();
            _startup!.SetRunAtStartup(now);
            item.IsChecked = SafeIsEnabled();
        });
        return item;
    }

    private bool SafeIsEnabled()
    {
        try { return _startup!.IsRunAtStartupEnabled(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return false; }
    }

    // ── data folder ─────────────────────────────────────────────────────────────────────────

    private NativeMenuItem BuildFolderItem()
    {
        var item = new NativeMenuItem(_text("Tray_OpenFolder", "Open data folder"));
        item.Command = new RelayCommand(() => _openFolder(DataFolder));
        return item;
    }

    private static void OpenInExplorer(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MasterHub.Core.Diagnostics.AppLog.Write($"Could not open the data folder: {ex.GetType().Name}");
        }
    }
}
