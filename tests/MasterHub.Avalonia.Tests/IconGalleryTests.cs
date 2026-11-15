using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using MasterHub.Avalonia.ViewModels.Inspector;
using MasterHub.Avalonia.Views.Controls;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Localization;

namespace MasterHub.UiTests;

public class IconGalleryTests
{
    [AvaloniaFact]
    public void Gallery_AllIcons_AndBrandLibrary()
    {
        var wrap = new WrapPanel { Margin = new Thickness(16), Width = 900 };
        var keys = new[] { "Studio", "Profiles", "Store", "Logs", "Settings", "Standby", "Macro", "Razer", "ObsLogo", "Discord", "Twitch", "Kick",
            "Spotify", "Vlc", "Voicemod", "JetKvm", "Teams", "HomeAssistant", "YouTube", "Streamlabs", "Elgato", "Mouse", "Keyboard", "Volume", "Mute",
            "Play", "Computer", "Speed", "Cast", "Bolt", "Compass", "Bulb", "Apps", "Folder", "Globe", "Link", "Plug", "Key", "Chat", "Target", "Clock",
            "Image", "Power", "Video", "Headphones", "Close", "Trash", "Rotate", "Download", "Upload" };
        foreach (var k in keys)
        {
            var icon = new PathIcon { Width = 22, Height = 22, Foreground = (global::Avalonia.Media.IBrush)Application.Current!.FindResource("AccentPrimaryBrush")! };
            icon.Bind(PathIcon.DataProperty, new global::Avalonia.Data.Binding { Source = k, Converter = IconConverters.ByKey });
            var tile = new Border { Child = new Panel { Children = { icon } }, Margin = new Thickness(6) };
            tile.Classes.Add("iconTile");
            var cell = new StackPanel { Width = 90, Children = { tile, new TextBlock { Text = k, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center } } };
            wrap.Children.Add(cell);
        }
        Shot.Render(wrap, "icon_gallery", 940, 520);

        var vm = new ActionLibraryViewModel();
        vm.SelectCategory(LocalizationService.Instance.GetString("Cat_Razer"));
        Shot.Render(new ActionLibraryView { DataContext = vm }, "library_razer", 380, 640);
        Assert.NotEmpty(vm.FilteredItems);
    }
}
