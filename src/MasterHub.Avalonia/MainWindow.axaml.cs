using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using MasterHub.Avalonia.Services;
using MasterHub.Avalonia.ViewModels;

namespace MasterHub.Avalonia;

public partial class MainWindow : Window
{
    private readonly IMasterHubDeviceService? _deviceService;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => UpdateCompactLayout();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ClientSizeProperty) UpdateCompactLayout();
        };
    }

    public MainWindow(MainViewModel viewModel, IMasterHubDeviceService deviceService, ITrayService trayService) : this()
    {
        DataContext = viewModel;
        _deviceService = deviceService;
        Icon = TrayService.CreateAppIcon();
        trayService.Initialize(this);

        Opened += (_, _) => FitToScreen();
        UpdateCompactLayout();

        PropertyChanged += (s, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                var canvas = App.Services?.GetService<MasterHub.Avalonia.ViewModels.Canvas.CanvasViewModel>();
                if (WindowState == WindowState.Minimized)
                {
                    if (canvas != null) canvas.IsUiSuspended = true;
                    TrayService.OptimizeMemoryUsage();
                }
                else
                {
                    if (canvas != null) canvas.IsUiSuspended = false;
                }
            }
        };
    }

    /// <summary>Poniżej tej szerokości (px) pasek boczny zwija się do ikon, aby Studio z inspektorem mieściło się na ekranie.</summary>
    private const double CompactWidth = 1500;

    private void UpdateCompactLayout()
    {
        if (DataContext is MainViewModel vm)
            vm.Navigation.IsAutoCollapsed = ClientSize.Width > 0 && ClientSize.Width < CompactWidth;
    }

    /// <summary>Okno startuje 1440x900 - na mniejszym ekranie (lub przy skalowaniu 125-150%) wystawałoby poza obszar roboczy.</summary>
    private void FitToScreen()
    {
        var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea;
        var scale = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.Scaling ?? 1.0;
        if (area == null) return;
        double maxW = area.Value.Width / scale, maxH = area.Value.Height / scale;
        if (Width > maxW || Height > maxH)
        {
            Width = Math.Max(MinWidth, Math.Min(Width, maxW));
            Height = Math.Max(MinHeight, Math.Min(Height, maxH));
            Position = new global::Avalonia.PixelPoint(area.Value.X, area.Value.Y);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        try
        {
            App.ShutdownHardware();
        }
        catch { }
    }
}