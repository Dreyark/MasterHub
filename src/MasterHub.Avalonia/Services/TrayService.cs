using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    private System.Threading.Timer? _periodicTrimTimer;
    private long _lastOptimizedWsMb;
    private DateTime _lastOptimizeUtc = DateTime.MinValue;

    public bool IsMinimizedToTray { get; private set; }
    public bool IsExiting { get; private set; }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);

    private readonly MasterHub.Core.Profiles.ISmartProfileService? _smart;

    private readonly MasterHub.Core.Profiles.IProfileService? _profileStore;
    private readonly MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel? _profilesPage;
    private readonly MasterHub.Core.SystemControl.IStartupService? _startup;

    public TrayService(CanvasViewModel canvas, MasterHub.Core.Profiles.ISmartProfileService? smartProfiles = null,
        MasterHub.Core.Profiles.IProfileService? profileStore = null,
        MasterHub.Avalonia.ViewModels.Pages.ProfilesPageViewModel? profilesPage = null,
        MasterHub.Core.SystemControl.IStartupService? startup = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _smart = smartProfiles;
        _profileStore = profileStore;
        _profilesPage = profilesPage;
        _startup = startup;
    }

    /// <summary>Switches to a profile picked in the tray menu, the same way a click on the Profiles page does.</summary>
    private void ActivateFromTray(string profileId)
    {
        if (_profilesPage == null) return;
        var item = _profilesPage.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (item == null)
        {
            _profilesPage.RefreshProfiles();
            item = _profilesPage.Profiles.FirstOrDefault(p => p.Id == profileId);
        }
        if (item != null) _profilesPage.ActivateProfileCommand.Execute(item);
    }

    public void Initialize(Window window)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        var icons = TrayIcon.GetIcons(Application.Current!) ?? new TrayIcons();
        if (TrayIcon.GetIcons(Application.Current!) == null) TrayIcon.SetIcons(Application.Current!, icons);

        _trayIcon = new TrayIcon { ToolTipText = "Cooler Master MasterHub", IsVisible = true, Icon = CreateAppIcon() };
        _trayIcon.Clicked += (_, _) => RestoreFromTray();

        var menu = new NativeMenu();
        string openText = MasterHub.Core.Localization.LocalizationService.Instance.GetString("Tray_Open", "Otwórz MasterHub");
        string exitText = MasterHub.Core.Localization.LocalizationService.Instance.GetString("Tray_Exit", "Zakończ");
        var openItem = new NativeMenuItem(openText);
        openItem.Click += (_, _) => RestoreFromTray();
        var exitItem = new NativeMenuItem(exitText);
        exitItem.Click += (_, _) => ExitApplication();

        menu.Add(openItem);
        menu.Add(new NativeMenuItemSeparator());
        new TrayMenuBuilder(_profileStore, _profilesPage != null ? ActivateFromTray : null, _startup).AddTo(menu);
        AddAutoSwitchItem(menu);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exitItem);
        _trayIcon.Menu = menu;
        icons.Add(_trayIcon);

        // Okresowe sprzątanie pamięci. Wcześniej co 2 minuty w trayu: wyczyszczenie cache'y obrazów, agresywny blokujący
        // GC z kompaktowaniem i obcięcie working setu - to powodowało cykliczne skoki CPU (ponowne renderowanie klawiszy,
        // page-faulty po obcięciu) bez realnej oszczędności pamięci prywatnej. Teraz: rzadziej (co 5 min sprawdzamy),
        // a sprzątamy tylko gdy proces realnie urósł od ostatniego sprzątania.
        _periodicTrimTimer = new System.Threading.Timer(_ =>
        {
            try
            {
                using var proc = Process.GetCurrentProcess();
                long wsMb = proc.WorkingSet64 / (1024 * 1024);
                bool grew = wsMb > _lastOptimizedWsMb + 40;
                bool cooldownOver = DateTime.UtcNow - _lastOptimizeUtc > TimeSpan.FromMinutes(10);
                if (cooldownOver && (grew || wsMb > 220))
                {
                    OptimizeMemoryUsage();
                    using var after = Process.GetCurrentProcess();
                    _lastOptimizedWsMb = after.WorkingSet64 / (1024 * 1024);
                    _lastOptimizeUtc = DateTime.UtcNow;
                }
            }
            catch { }
        }, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

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
            using var outerRect = new SKRoundRect(new SKRect(2, 2, s - 2, s - 2), 14, 14);
            c.DrawRoundRect(outerRect, bg);

            using var inner = new SKPaint { IsAntialias = true, Color = SKColor.Parse("#1A1C23"), Style = SKPaintStyle.Fill };
            using var innerRect = new SKRoundRect(new SKRect(6, 6, s - 6, s - 6), 10, 10);
            c.DrawRoundRect(innerRect, inner);

            using var tf = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
            using var font = new SKFont(tf, 30f);
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
            MasterHub.Core.Rendering.MediaFrameExtractor.ClearCache();
            MasterHub.Core.Rendering.WindowsAppIconExtractor.ClearCache();
            MasterHub.Core.Rendering.ButtonImageRenderer.ClearCache();
            MasterHub.Avalonia.ViewModels.Pages.PluginCatalogItemViewModel.ClearCache();

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
        _periodicTrimTimer?.Dispose();
        _periodicTrimTimer = null;
        if (_trayIcon == null) return;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
    }
}
