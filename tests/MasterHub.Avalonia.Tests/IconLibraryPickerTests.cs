using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Inspector;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Profiles.Packages;
using MasterHub.Core.Rendering.Icons;

namespace MasterHub.UiTests;

/// <summary>T-21a: searchable icon library picker that puts a tinted icon on the key.</summary>
public class IconLibraryPickerTests
{
    [AvaloniaFact]
    public void ThePicker_ShowsResults_WithPreviews_AndFollowsTheQuery()
    {
        var picker = new IconPickerViewModel();

        Assert.Equal(IconPickerViewModel.MaxResults, picker.Results.Count);
        picker.Query = "microphone";
        Assert.NotEmpty(picker.Results);
        Assert.All(picker.Results, r => Assert.NotNull(r.Preview));

        picker.Query = "zzzz-nothing-here";
        Assert.True(picker.HasNoResults);
    }

    [AvaloniaFact]
    public void PickingAnIcon_RaisesTheNameAndTheColour()
    {
        var picker = new IconPickerViewModel();
        (string name, string tint)? got = null;
        picker.Picked += (n, t) => got = (n, t);

        picker.SelectTintCommand.Execute("#FACC15");
        picker.PickCommand.Execute("house");
        picker.PickCommand.Execute("no-such-icon");   // ignored

        Assert.Equal(("house", "#FACC15"), got);
    }

    [AvaloniaFact]
    public void ApplyingALibraryIcon_SetsTheKeyPicture_ForTheStateBeingEdited()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_iconpick_" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MediaStore(dir);
            var key = new KeyItemViewModel { Index = 0 };

            Assert.True(key.ApplyLibraryIcon("house", "#ffffff", IconLibrary.Shared, store));
            Assert.True(store.IsStorePath(key.ImagePath));
            Assert.Equal("Contain", key.ImageFitMode);
            Assert.True(key.HasCustomImage);

            key.SelectedEditState = 1;
            Assert.True(key.ApplyLibraryIcon("mic", "#ff0000", IconLibrary.Shared, store));
            Assert.True(store.IsStorePath(key.ActiveImagePath));
            Assert.NotEqual(key.ImagePath, key.ActiveImagePath);

            Assert.False(key.ApplyLibraryIcon("not-an-icon", "#fff", IconLibrary.Shared, store));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void APhotoPickedAfterALibraryIcon_FillsTheWholeKey_InsteadOfKeepingContainAndTheOldCrop()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_photo_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var store = new MediaStore(Path.Combine(dir, "store"));
            var key = new KeyItemViewModel { Index = 0 };
            Assert.True(key.ApplyLibraryIcon("house", "#ffffff", IconLibrary.Shared, store));
            key.ImageScale = 250;
            key.ImageOffsetX = 40;
            key.ImageOffsetY = -30;

            string photo = Path.Combine(dir, "photo.png");
            using (var bmp = new SkiaSharp.SKBitmap(200, 120))
            using (var img = SkiaSharp.SKImage.FromBitmap(bmp))
            using (var data = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
                File.WriteAllBytes(photo, data.ToArray());

            Assert.True(key.ApplyMediaFile(photo, isActiveState: false));

            Assert.Equal("Cover", key.ImageFitMode);
            Assert.Equal(100, key.ImageScale);
            Assert.Equal(0, key.ImageOffsetX);
            Assert.Equal(0, key.ImageOffsetY);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void ThePickerView_Renders()
    {
        var vm = new IconPickerViewModel { Query = "arrow" };
        string path = Shot.Render(new IconLibraryPickerView { DataContext = vm }, "icon_library_picker", 360, 460);

        Assert.True(new FileInfo(path).Length > 2000);
    }

    [AvaloniaFact]
    public void TheImageTab_HasTheIconLibraryButton()
    {
        var window = new Window { Width = 360, Height = 900, Content = new KeyDisplaySettingsView { DataContext = new KeyItemViewModel { Index = 1 } } };
        window.Show();

        string label = MasterHub.Core.Localization.LocalizationService.Instance.GetString("IconLib_Open", "Icons");
        Assert.Contains(global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<Button>(), b => Equals(b.Content, label));
        window.Close();
    }
}
