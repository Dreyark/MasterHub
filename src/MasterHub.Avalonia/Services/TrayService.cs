using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MasterHub.Avalonia.ViewModels.Canvas;
using SkiaSharp;

namespace MasterHub.Avalonia.Services;

public sealed partial class TrayService : ITrayService
{
    private readonly CanvasViewModel _canvas;
    private Window? _window;
    private TrayIcon? _trayIcon;

    public bool IsMinimizedToTray { get; private set; }
    public bool IsExiting { get; private set; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    public TrayService(CanvasViewModel canvas) => _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));

    public void Initialize(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        var icons = TrayIcon.GetIcons(Application.Current!) ?? new TrayIcons();
        if (TrayIcon.GetIcons(Application.Current!) == null) TrayIcon.SetIcons(Application.Current!, icons);

        _trayIcon = new TrayIcon { ToolTipText = "Cooler Master MasterHub", IsVisible = true, Icon = CreateAppIcon() };
        _trayIcon.Clicked += (_, _) => RestoreFromTray();

        var menu = new NativeMenu();
        var openItem = new NativeMenuItem("Otwórz MasterHub");
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new NativeMenuItem("Zakończ");
        exitItem.Click += (_, _) => ExitApplication();

        menu.Add(openItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);
        _trayIcon.Menu = menu;
        icons.Add(_trayIcon);
    }

    public void MinimizeToTray()
    {
        if (_window == null || IsMinimizedToTray) return;
        IsMinimizedToTray = true;
        _canvas.IsUiSuspended = true;
        _window.Hide();
        OptimizeMemoryUsage();
    }

    public void RestoreFromTray()
    {
        if (_window == null) return;
        IsMinimizedToTray = false;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        _canvas.IsUiSuspended = false;
    }

    public void ExitApplication()
    {
        IsExiting = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            _window?.Close();
    }

    public static WindowIcon CreateAppIcon()
    {
        const int s = 64;
        using var bmp = new SKBitmap(s, s, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var c = new SKCanvas(bmp))
        {
            c.Clear(SKColors.Transparent);
            using var bg = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#5E81F4"), Style = SKPaintStyle.Fill };
            c.DrawRoundRect(new SKRoundRect(new SKRect(2, 2, s - 2, s - 2), 14, 14), bg);

            using var inner = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#1A1C23"), Style = SKPaintStyle.Fill };
            c.DrawRoundRect(new SKRoundRect(new SKRect(6, 6, s - 6, s - 6), 10, 10), inner);

            using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 30f);
            using var txt = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#81A1C1"), Style = SKPaintStyle.Fill };
            c.DrawText("M", s / 2f, s / 2f + 11f, SKTextAlign.Center, font, txt);
        }
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return new WindowIcon(new MemoryStream(data.ToArray()));
    }

    public static void OptimizeMemoryUsage()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: false, compacting: true);
            GC.WaitForPendingFinalizers();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var p = Process.GetCurrentProcess();
                SetProcessWorkingSetSize(p.Handle, (IntPtr)(-1), (IntPtr)(-1));
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_trayIcon == null) return;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }
}
