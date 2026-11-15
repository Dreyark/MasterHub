using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using MasterHub.Core.Actions;

namespace MasterHub.Core.SystemControl;

/// <summary>The program in front: its process and whether it runs as administrator (<c>null</c> = could not tell).</summary>
public sealed record ForegroundTarget(int ProcessId, string ProcessName, bool? IsElevated);

/// <summary>Whether MasterHub and the program in front run elevated (T-34 #12).</summary>
public interface IElevationProbe
{
    bool IsSelfElevated { get; }
    ForegroundTarget? GetForeground();
}

/// <summary>
/// The elevated-app warning (T-34 #12): Windows (UIPI) <b>silently drops</b> synthetic key presses that a normal program
/// sends to a program running as administrator. Before a key sends keystrokes we look at the program in front and, when the
/// presses will be dropped, say why (the keys are still sent – the user may know better).
/// </summary>
public static class ElevatedInputGuard
{
    /// <summary>Actions that type or press keys into the program in front.</summary>
    public static bool SendsKeystrokes(DeckActionType action) =>
        action is DeckActionType.Hotkey or DeckActionType.HotkeySwitch or DeckActionType.TextPaste or DeckActionType.MacroPlay;

    /// <summary>True when the key presses will not arrive: the target runs elevated and MasterHub does not.</summary>
    public static bool InputWillBeDropped(bool selfElevated, ForegroundTarget? foreground) =>
        !selfElevated && foreground?.IsElevated == true;
}

/// <summary>Windows implementation: the foreground window's process token (<c>TokenElevation</c>).</summary>
public sealed class WindowsElevationProbe : IElevationProbe
{
    private readonly Lazy<bool> _self = new(() =>
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    });

    public bool IsSelfElevated => _self.Value;

    public ForegroundTarget? GetForeground()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero) return null;
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == 0) return null;

            string name;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch
            {
                name = "pid " + pid;
            }
            return new ForegroundTarget((int)pid, name, IsElevated(pid));
        }
        catch
        {
            return null;
        }
    }

    private static bool? IsElevated(uint pid)
    {
        IntPtr process = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (process == IntPtr.Zero)
        {
            // a normal program may not even query an elevated / protected one
            return Marshal.GetLastWin32Error() == ErrorAccessDenied ? true : null;
        }
        try
        {
            if (!OpenProcessToken(process, TokenQuery, out IntPtr token))
            {
                return Marshal.GetLastWin32Error() == ErrorAccessDenied ? true : null;
            }
            try
            {
                int size = Marshal.SizeOf<int>();
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    if (!GetTokenInformation(token, TokenElevationClass, buffer, size, out _)) return null;
                    return Marshal.ReadInt32(buffer) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const int TokenElevationClass = 20;
    private const int ErrorAccessDenied = 5;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
