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
    }

    public MainWindow(MainViewModel viewModel, IMasterHubDeviceService deviceService, ITrayService trayService) : this()
    {
        DataContext = viewModel;
        _deviceService = deviceService;
        Icon = TrayService.CreateAppIcon();
        trayService.Initialize(this);


    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        try
        {
            var deviceService = _deviceService ?? App.Services?.GetService<IMasterHubDeviceService>();
            deviceService?.Dispose();
        }
        catch { }
    }
}