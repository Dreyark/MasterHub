using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using MasterHub.Core.Native;

namespace MasterHub.Core.Devices;

/// <summary>
/// Strażnik sprzętu MasterHub (Hardware Watchdog).
/// Zapewnia wygaszenie ekranów i diod LED w przypadku awarii (crash) lub nagłego zakończenia
/// procesu przez Menadżer zadań / zabicie procesu (SIGKILL / TerminateProcess).
/// </summary>
public static class HardwareWatchdog
{
    private const string CleanExitPrefix = "MasterHub_CleanExit_";
    private static EventWaitHandle? s_cleanExitEvent;
    private static string? s_flagFilePath;
    private static Process? s_watchdogProcess;
    private static int s_initialized;

    private static string GetEventName(int pid) => $"{CleanExitPrefix}{pid}";
    private static string GetFlagFilePath(int pid) => Path.Combine(Path.GetTempPath(), $"masterhub_clean_{pid}.flag");

    /// <summary>
    /// Inicjalizuje mechanizm strażnika: rejestruje uchwyty błędów procesu głównego
    /// oraz uruchamia ukryty proces monitorujący w tle.
    /// </summary>
    public static void Initialize()
    {
        if (Interlocked.Exchange(ref s_initialized, 1) != 0) return;

        try
        {
            int currentPid = Environment.ProcessId;
            s_flagFilePath = GetFlagFilePath(currentPid);
            try { if (File.Exists(s_flagFilePath)) File.Delete(s_flagFilePath); } catch { }

            s_cleanExitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, GetEventName(currentPid));

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            s_watchdogProcess = StartWatchdogProcess(currentPid);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HardwareWatchdog] Błąd inicjalizacji: {ex.Message}");
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            MasterHubManager.ExtinguishAllHardware();
        }
        catch { }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // W .NET Core nieobserwowany wyjątek Taska NIE kończy procesu. Wcześniej każdy taki wyjątek
        // (np. z "fire-and-forget" Task.Run, wyzwalany losowo przez GC) gasił ekrany i LED-y całego decka
        // mimo działającej aplikacji. Teraz tylko oznaczamy go jako obsłużony.
        e.SetObserved();
    }

    /// <summary>
    /// Sygnalizuje czyste i zamierzone wyjście z aplikacji (np. kliknięcie [X] lub Zakończ w zasobniku).
    /// Dzięki temu proces strażnika po wyjściu aplikacji nie podejmuje zbędnych działań.
    /// </summary>
    public static void SignalCleanExit()
    {
        try
        {
            s_cleanExitEvent?.Set();
            if (!string.IsNullOrEmpty(s_flagFilePath))
            {
                File.WriteAllText(s_flagFilePath, "clean");
            }
        }
        catch { }
    }

    /// <summary>
    /// Uruchamia ukryty proces potomny z flagą --watchdog <pid>.
    /// </summary>
    private static Process? StartWatchdogProcess(int currentPid)
    {
        try
        {
            string? procPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(procPath)) return null;

            var psi = new ProcessStartInfo
            {
                FileName = procPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (procPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                procPath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string dllPath = typeof(HardwareWatchdog).Assembly.Location;
                string avaloniaDll = Path.Combine(Path.GetDirectoryName(dllPath) ?? "", "MasterHub.Avalonia.dll");
                psi.Arguments = File.Exists(avaloniaDll)
                    ? $"\"{avaloniaDll}\" --watchdog {currentPid}"
                    : $"\"{dllPath}\" --watchdog {currentPid}";
            }
            else
            {
                psi.Arguments = $"--watchdog {currentPid}";
            }

            return Process.Start(psi);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Główna pętla procesu strażnika (Watchdog).
    /// Oczekuje na zakończenie procesu nadrzędnego. W razie wykrycia zabicia procesu
    /// lub awarii natychmiast wygasza wszystkie podłączone moduły sprzętowe.
    /// </summary>
    public static void RunWatchdogProcess(int parentPid)
    {
        string eventName = GetEventName(parentPid);
        string flagFile = GetFlagFilePath(parentPid);

        EventWaitHandle? cleanExitEvent = null;
        try
        {
            cleanExitEvent = EventWaitHandle.OpenExisting(eventName);
        }
        catch
        {
            try { cleanExitEvent = new EventWaitHandle(false, EventResetMode.ManualReset, eventName); } catch { }
        }

        Process? parentProc = null;
        try
        {
            parentProc = Process.GetProcessById(parentPid);
        }
        catch
        {
            // Proces rodzica już nie żyje
        }

        if (parentProc != null)
        {
            try
            {
                parentProc.WaitForExit();
            }
            catch { }
        }

        // Poczekaj na ewentualny sygnał czystego zamknięcia
        bool wasCleanExit = false;
        try
        {
            if (cleanExitEvent != null && cleanExitEvent.WaitOne(250))
            {
                wasCleanExit = true;
            }
            else if (File.Exists(flagFile))
            {
                wasCleanExit = true;
            }
        }
        catch { }

        // Jeśli proces nie został zamknięty czysto (crash lub zabicie w Menadżerze zadań)
        if (!wasCleanExit)
        {
            // Daj systemowi Windows 100ms na zwolnienie uchwytów USB
            Thread.Sleep(100);

            // Wygaś moduły MasterHub (ponawiaj próbę do 5 razy w razie zwłoki sterownika USB)
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (MasterHubUsbTransport.ExtinguishAllConnectedDevices())
                    {
                        break;
                    }
                }
                catch { }
                Thread.Sleep(150);
            }
        }

        try { if (File.Exists(flagFile)) File.Delete(flagFile); } catch { }
        try { cleanExitEvent?.Dispose(); } catch { }
        try { parentProc?.Dispose(); } catch { }
    }
}
