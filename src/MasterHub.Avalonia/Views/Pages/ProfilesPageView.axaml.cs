using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.Views.Pages;

public partial class ProfilesPageView : UserControl
{
    public ProfilesPageView()
    {
        InitializeComponent();
    }

    private async void OnImportProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesPageViewModel vm) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = LocalizationService.Instance.GetString("Profiles_ImportDialogTitle", "Wybierz plik profilu JSON"),
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("MasterHub Profile (*.json)")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        });

        if (files.Count > 0)
        {
            vm.ImportProfile(files[0].Path.LocalPath);
        }
    }

    private async void OnExportProfileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilesPageViewModel vm) return;
        if (sender is not Control { Tag: ProfileItemViewModel item }) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.Instance.GetString("Profiles_ExportDialogTitle", "Eksportuj profil"),
            DefaultExtension = "json",
            SuggestedFileName = $"{item.Name}.json",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("MasterHub Profile (*.json)")
                {
                    Patterns = new[] { "*.json" }
                }
            }
        });

        if (file != null)
        {
            vm.ExportProfile(item, file.Path.LocalPath);
        }
    }
}
