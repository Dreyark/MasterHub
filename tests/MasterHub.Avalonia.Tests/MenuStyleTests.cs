using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace MasterHub.UiTests;

/// <summary>Menus (tray, right-click) use the app's colours, not the default Fluent grey.</summary>
public class MenuStyleTests
{
    private static ContextMenu Sample() => new()
    {
        Width = 240,
        ItemsSource = new object[]
        {
            new MenuItem { Header = "Open MasterHub" },
            new Separator(),
            new MenuItem { Header = "Gaming", ToggleType = MenuItemToggleType.Radio, IsChecked = true },
            new MenuItem { Header = "Work", ToggleType = MenuItemToggleType.Radio },
            new Separator(),
            new MenuItem { Header = "Start with Windows", ToggleType = MenuItemToggleType.CheckBox, IsChecked = true },
            new MenuItem { Header = "Exit" }
        }
    };

    [AvaloniaFact]
    public void TheMenu_UsesTheAppColoursAndRoundedCorners()
    {
        var menu = Sample();
        string path = Shot.Render(menu, "menu_style", 300, 330);
        var window = new Window { Content = Sample(), Width = 300, Height = 330 };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var m = (ContextMenu)window.Content!;
        Assert.Equal(Color.Parse("#24274A"), ((ISolidColorBrush)m.Background!).Color);
        Assert.Equal(new global::Avalonia.CornerRadius(10), m.CornerRadius);
        Assert.True(new FileInfo(path).Length > 1000);
        window.Close();
    }
}
