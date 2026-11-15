using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;

namespace MasterHub.UiTests;

public class StyleGalleryTests
{
    private static IBrush B(string key) => (IBrush)Application.Current!.FindResource(key)!;

    private static Button Btn(string text, string cls) => ButtonWith(text, cls);

    private static Button ButtonWith(string text, string cls)
    {
        var b = new Button { Content = text, Margin = new Thickness(4) };
        foreach (var c in cls.Split(' ', StringSplitOptions.RemoveEmptyEntries)) b.Classes.Add(c);
        return b;
    }

    private static Control Section(string title, params Control[] items)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) panel.Children.Add(i);
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Spacing = 4 };
        var tb = new TextBlock { Text = title };
        tb.Classes.Add("caption");
        sp.Children.Add(tb);
        sp.Children.Add(panel);
        return sp;
    }

    [AvaloniaFact]
    public void Gallery_Controls()
    {
        var root = new StackPanel { Margin = new Thickness(16), Spacing = 2 };
        root.Children.Add(Section("Buttons: actionBtn / accentBtn / pillBtn(+active) / iconBtn / ghostBtn / plain",
            Btn("actionBtn", "actionBtn"), Btn("tabActive", "actionBtn tabActive"),
            Btn("accentBtn", "accentBtn"), Btn("pillBtn", "pillBtn"), Btn("pill.active", "pillBtn active"),
            Btn("iconBtn", "iconBtn"), Btn("ghostBtn", "ghostBtn"), Btn("plain", ""),
            new Button { Content = "disabled accent", IsEnabled = false, Classes = { "accentBtn" } }));
        root.Children.Add(Section("CheckBox / ToggleSwitch / RadioButton / Slider / ProgressBar (kolor akcentu)",
            new CheckBox { Content = "checked", IsChecked = true, Margin = new Thickness(4) },
            new CheckBox { Content = "unchecked", Margin = new Thickness(4) },
            new ToggleSwitch { IsChecked = true, Margin = new Thickness(4) },
            new RadioButton { Content = "radio", IsChecked = true, Margin = new Thickness(4) },
            new Slider { Width = 140, Value = 60, Margin = new Thickness(4) },
            new ProgressBar { Width = 140, Value = 60, Margin = new Thickness(4) }));
        root.Children.Add(Section("TextBox / ComboBox / Tabs / ListBox",
            new TextBox { Text = "TextBox", Width = 140, Margin = new Thickness(4) },
            new ComboBox { ItemsSource = new[] { "One", "Two" }, SelectedIndex = 0, Width = 140, Margin = new Thickness(4) },
            new TabControl
            {
                Width = 240,
                Height = 80,
                ItemsSource = new[] { new TabItem { Header = "Tab A", Content = "A" }, new TabItem { Header = "Tab B", Content = "B" } }
            },
            new ListBox { Width = 140, Height = 80, ItemsSource = new[] { "item 1", "item 2", "item 3" }, SelectedIndex = 1 }));
        var card = new Border { Width = 240, Height = 60, Margin = new Thickness(4), Child = new TextBlock { Text = "glassCard" } };
        card.Classes.Add("glassCard");
        var chip = new Border { Child = new TextBlock { Text = "inspectorSection" }, Width = 160, Height = 50, Margin = new Thickness(4) };
        chip.Classes.Add("inspectorSection");
        root.Children.Add(Section("Cards", card, chip));
        var texts = new[] { "h1", "h2", "h3", "caption", "dim", "badge" }
            .Select(c => { var t = new TextBlock { Text = c + " text", Margin = new Thickness(6) }; t.Classes.Add(c); return (Control)t; }).ToArray();
        root.Children.Add(Section("Typography", texts));

        string path = Shot.Render(root, "gallery_controls", 1000, 640);
        Assert.True(File.Exists(path));
    }
}
