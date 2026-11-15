using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MasterHub.Core.SystemControl;

/// <summary>
/// Pomocnik sprawdzania uruchomionych procesów. Wiele integracji (OBS, VLC, Discord, Teams, Voicemod, warunki akcji)
/// regularnie pyta "czy proces X działa?". <c>Process.GetProcessesByName</c> tworzy obiekt Process dla każdego procesu
/// w systemie (~1-3 ms CPU i setki alokacji na wywołanie), więc zamiast tego używamy jednego, współdzielonego
/// i krótko cache'owanego zrzutu nazw procesów (Toolhelp32 - bez otwierania uchwytów procesów).
/// </summary>
public static class ProcessHelper
{
    /// <summary>Jak długo zrzut procesów jest uznawany za aktualny.</summary>
    public static TimeSpan SnapshotLifetime { get; set; } = TimeSpan.FromMilliseconds(1500);

    private static readonly object s_lock = new();
    private static HashSet<string>? s_names;
    private static long s_snapshotTicks;

    /// <summary>
    /// Sprawdza, czy proces o podanej nazwie (bez rozszerzenia, bez rozróżniania wielkości liter) jest uruchomiony.
    /// </summary>
    public static bool IsProcessRunning(string processName) => IsProcessRunning(processName, allowCached: true);

    /// <param name="allowCached">false = wymuś świeży zrzut procesów (pomija krótki cache).</param>
    public static bool IsProcessRunning(string processName, bool allowCached)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        string cleanName = Path.GetFileNameWithoutExtension(processName).Trim();
        if (cleanName.Length == 0) return false;

        var names = GetSnapshot(allowCached);
        if (names != null) return names.Contains(cleanName);

        return IsProcessRunningSlow(cleanName);
    }

    /// <summary>Sprawdza, czy którykolwiek z podanych procesów jest uruchomiony (jeden zrzut dla wszystkich nazw).</summary>
    public static bool IsAnyProcessRunning(params string[] processNames)
    {
        if (processNames == null || processNames.Length == 0) return false;

        foreach (var name in processNames)
        {
            if (IsProcessRunning(name)) return true;
        }

        return false;
    }

    /// <summary>Unieważnia cache (np. po uruchomieniu procesu przez aplikację).</summary>
    public static void InvalidateCache()
    {
        lock (s_lock) s_names = null;
    }

    private static HashSet<string>? GetSnapshot(bool allowCached)
    {
        lock (s_lock)
        {
            long now = Stopwatch.GetTimestamp();
            if (allowCached && s_names != null && Stopwatch.GetElapsedTime(s_snapshotTicks, now) < SnapshotLifetime)
            {
                return s_names;
            }

            var fresh = TakeToolhelpSnapshot();
            if (fresh == null) return null;
            s_names = fresh;
            s_snapshotTicks = now;
            return s_names;
        }
    }

    private static HashSet<string>? TakeToolhelpSnapshot()
    {
        IntPtr snap = IntPtr.Zero;
        try
        {
            snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return null;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
            if (!Process32FirstW(snap, ref entry)) return set;
            do
            {
                string exe = entry.szExeFile;
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = exe[..^4];
                set.Add(exe);
            }
            while (Process32NextW(snap, ref entry));
            return set;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (snap != IntPtr.Zero && snap != new IntPtr(-1)) CloseHandle(snap);
        }
    }

    // Ścieżka awaryjna (gdyby Toolhelp był niedostępny): stare zachowanie z deterministycznym zwalnianiem uchwytów.
    private static bool IsProcessRunningSlow(string cleanName)
    {
        Process[] procs;
        try
        {
            procs = Process.GetProcessesByName(cleanName);
        }
        catch
        {
            return false;
        }

        try
        {
            return procs.Length > 0;
        }
        finally
        {
            for (int i = 0; i < procs.Length; i++)
            {
                try { procs[i].Dispose(); } catch { }
            }
        }
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
