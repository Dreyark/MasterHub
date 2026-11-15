using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>
/// Own language files (T-34 #11): "Make a language file" writes every English text into
/// <c>%APPDATA%\MasterHub\Languages\template.json</c>; a translated copy named after its code (<c>it.json</c>) shows up in
/// the language picker after a restart, and a text it lacks stays English.
/// </summary>
public partial class SettingsPageViewModel
{
    /// <summary>For tests: where the template is written (default: the user's languages folder).</summary>
    internal string? LanguagesFolderOverride { get; set; }

    [ObservableProperty]
    private string _languageFileStatus = "";

    public string LanguagesFolder => LanguagesFolderOverride ?? LocalizationService.UserLanguagesFolder;

    [RelayCommand]
    private void MakeLanguageFile()
    {
        var loc = LocalizationService.Instance;
        try
        {
            string path = loc.WriteLanguageTemplate(LanguagesFolder);
            LanguageFileStatus = string.Format(
                loc.GetString("Settings_LanguageFileSaved", "Saved {0}. Copy it as <code>.json (e.g. it.json), translate the texts and restart MasterHub."),
                path);
        }
        catch (Exception ex)
        {
            LanguageFileStatus = loc.GetString("Settings_LanguageFileError", "Could not write the language file: ") + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenLanguagesFolder()
    {
        try
        {
            Directory.CreateDirectory(LanguagesFolder);
            Process.Start(new ProcessStartInfo { FileName = LanguagesFolder, UseShellExecute = true });
        }
        catch
        {
            // no file manager: the path is in the status line anyway
        }
    }
}
