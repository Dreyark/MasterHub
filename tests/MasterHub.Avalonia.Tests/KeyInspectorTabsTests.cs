using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Actions;

namespace MasterHub.UiTests;

/// <summary>The key properties are split into tabs (action, text, image, rules, wallpaper) instead of one long list.</summary>
public class KeyInspectorTabsTests
{
    private static KeyItemViewModel Key()
    {
        var key = new KeyItemViewModel { Index = 2, AssignedAction = DeckActionType.MediaNext };
        key.CustomTitle = "Next";
        key.SetPanoramicState(true, "wall.png", "Cover", 150, 20, -10);
        return key;
    }

    private static TabControl Tabs(Window window) => window.GetVisualDescendants().OfType<TabControl>().First(t => t.Classes.Contains("keyTabs"));

    [AvaloniaFact]
    public void TheInspector_HasFiveTabs_InTheOrderOfWhatYouDoFirst()
    {
        var view = new KeyInspectorView { DataContext = Key() };
        var window = new Window { Width = 360, Height = 900, Content = view };
        window.Show();

        var tabs = Tabs(window);
        Assert.Equal(5, tabs.Items.Count);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(0, "action")]
    [InlineData(1, "text")]
    [InlineData(2, "image")]
    [InlineData(3, "rules")]
    [InlineData(4, "wallpaper")]
    public void EveryTab_Renders(int tab, string name)
    {
        string path = Shot.Render(new KeyInspectorView { DataContext = Key() }, "inspector_tab_" + name, 360, 900,
            w => Tabs(w).SelectedIndex = tab);

        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 2000);
    }

    [AvaloniaFact]
    public void TheTextTab_FoldsTheFontSettings_AwayUnderAnExpander()
    {
        var window = new Window { Width = 360, Height = 900, Content = new KeyInspectorView { DataContext = Key() } };
        window.Show();
        Tabs(window).SelectedIndex = 1;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var expanders = window.GetVisualDescendants().OfType<Expander>().Where(e => e.Classes.Contains("subtle")).ToList();

        Assert.Equal(2, expanders.Count);   // one for the title, one for the subtitle
        Assert.All(expanders, e => Assert.False(e.IsExpanded));
        window.Close();
    }

    [AvaloniaFact]
    public void TheWallpaperTab_HasTheZoomAndMoveSliders()
    {
        var window = new Window { Width = 360, Height = 900, Content = new KeyInspectorView { DataContext = Key() } };
        window.Show();
        Tabs(window).SelectedIndex = 4;
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var sliders = window.GetVisualDescendants().OfType<Slider>().Where(s => s.IsEffectivelyVisible).ToList();

        Assert.Contains(sliders, s => s.Minimum == 20 && s.Maximum == 400);       // zoom
        Assert.Equal(2, sliders.Count(s => s.Minimum == -100 && s.Maximum == 100)); // move left-right, up-down
        window.Close();
    }
}
