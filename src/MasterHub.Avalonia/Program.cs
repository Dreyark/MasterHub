using Avalonia;
using System;
using MasterHub.Core.Devices;

namespace MasterHub.Avalonia;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0].Equals("--watchdog", StringComparison.OrdinalIgnoreCase) && int.TryParse(args[1], out int parentPid))
        {
            HardwareWatchdog.RunWatchdogProcess(parentPid);
            return;
        }

        // Dwie instancje walczyłyby o wyłączny dostęp do USB HID oraz port 28123 hosta wtyczek Stream Deck
        // (typowo: autostart + ręczne uruchomienie). Druga instancja kończy się bez efektów ubocznych.
        using var singleInstanceMutex = new System.Threading.Mutex(true, @"Local\MasterHub.Avalonia.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance) return;

        HardwareWatchdog.Initialize();

        // Wyjątki w wątkach tła (pętla USB, Task.Run) omijają blok catch poniżej - bez tego handlera
        // proces padał bez śladu w crash.log. Wygaszanie sprzętu realizuje HardwareWatchdog.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog(e.ExceptionObject?.ToString() ?? "Nieznany wyjątek");
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            WriteCrashLog("[UnobservedTaskException] " + e.Exception);

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                MasterHubManager.ExtinguishAllHardware();
            }
            catch { }

            WriteCrashLog(ex.ToString());
            Console.Error.WriteLine(ex);
            throw;
        }
    }

    private static void WriteCrashLog(string text)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = System.IO.Path.Combine(appData, "MasterHub");
            System.IO.Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, "crash.log");
            // Dopisujemy zamiast nadpisywać - pierwsza przyczyna awarii nie może zostać zgubiona.
            if (System.IO.File.Exists(path) && new System.IO.FileInfo(path).Length > 512 * 1024)
            {
                System.IO.File.Delete(path);
            }
            System.IO.File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
