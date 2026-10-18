using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Avalonia.Services;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace MasterHub.Avalonia.Views.Window;

public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
    }

    private AvaloniaWindow? GetParentWindow()
    {
        return TopLevel.GetTopLevel(this) as AvaloniaWindow 
            ?? this.FindAncestorOfType<AvaloniaWindow>()
            ?? VisualRoot as AvaloniaWindow;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var window = GetParentWindow();
        if (window == null) return;

        if (e.ClickCount == 2)
        {
            window.WindowState = window.WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
            return;
        }

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            window.BeginMoveDrag(e);
        }
    }

    private void OnMinimizeToTrayClick(object? sender, RoutedEventArgs e)
    {
        var tray = App.Services?.GetService<ITrayService>();
        if (tray != null)
        {
            tray.MinimizeToTray();
        }
        else
        {
            var window = GetParentWindow();
            if (window != null) window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        if (window != null)
        {
            window.WindowState = WindowState.Minimized;
        }
    }

    private void OnMaximizeClick(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        if (window != null)
        {
            window.WindowState = window.WindowState == WindowState.Maximized 
                ? WindowState.Normal 
                : WindowState.Maximized;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        var window = GetParentWindow();
        if (window != null)
        {
            window.Close();
        }
    }
}
