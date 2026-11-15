using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Navigation;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Macros;
using NavigationPage = MasterHub.Avalonia.Services.NavigationPage;

namespace MasterHub.UiTests;

/// <summary>Atrapa dowolnego interfejsu: metody/właściwości zwracają wartości domyślne, zdarzenia są ignorowane.</summary>
public class NullProxy<T> : DispatchProxy where T : class
{
    public static T Create() => DispatchProxy.Create<T, NullProxy<T>>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var type = targetMethod?.ReturnType;
        if (type == null || type == typeof(void)) return null;
        if (type == typeof(Task)) return Task.CompletedTask;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}

/// <summary>Powłoka aplikacji (pasek boczny + panel strony) jak w MainWindow, bez sprzętu i bez prawdziwych ustawień.</summary>
public class ShellShotTests
{
    private static Control Shell(NavigationViewModel nav, Control page)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(new NavigationSidebarView { DataContext = nav });
        var panel = new Border { Child = page, Margin = new global::Avalonia.Thickness(0, 52, 16, 16) };
        panel.Classes.Add("pagePanel");
        Grid.SetColumn(panel, 1);
        grid.Children.Add(panel);
        return grid;
    }

    [AvaloniaFact]
    public void Shell_ExpandedSidebar_WithMacrosPage()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_shell_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var macros = new MacroService(new MacroStore(dir));
            var m = new MacroDefinition { Name = "Combo", Steps = { MacroStep.KeyDown(0x41), MacroStep.Delay(30), MacroStep.KeyUp(0x41) } };
            macros.SaveMacro(m);
            var nav = new NavigationViewModel(new NavigationService(), NullProxy<IMasterHubDeviceService>.Create());
            nav.SelectPage(NavigationPage.Macros);

            string path = Shot.Render(Shell(nav, new MacrosPageView { DataContext = new MacrosPageViewModel(macros) }), "shell_expanded_macros", 1440, 900);

            Assert.True(File.Exists(path));
            Assert.Equal("NavLabel_Macros", NavigationViewModel.PageTitleKey(nav.SelectedPage));
            Assert.False(string.IsNullOrWhiteSpace(nav.CurrentPageTitle));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [AvaloniaFact]
    public void Shell_CollapsedSidebar_WithLogsPage()
    {
        var device = NullProxy<IMasterHubDeviceService>.Create();
        var nav = new NavigationViewModel(new NavigationService(), device);
        nav.SelectPage(NavigationPage.Logs);
        nav.ToggleSidebar();

        string path = Shot.Render(Shell(nav, new LogsPageView { DataContext = new LogsPageViewModel(device) }), "shell_collapsed_logs", 1200, 700);

        Assert.True(File.Exists(path));
        Assert.True(nav.IsSidebarCollapsed);
    }
}
