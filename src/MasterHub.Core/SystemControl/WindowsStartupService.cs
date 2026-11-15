using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MasterHub.Core.SystemControl;

/// <summary>
/// Implementacja autostartu dla systemu Windows wykorzystująca rejestr użytkownika (HKCU\...\Run).
/// Nie wymaga uprawnień administratora (UAC).
/// </summary>
public class WindowsStartupService : IStartupService
{
    private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MasterHub";

    public bool IsRunAtStartupEnabled()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
            var value = key?.GetValue(AppName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    public bool SetRunAtStartup(bool enable, bool startMinimized = true)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return false;

            if (enable)
            {
                string exePath = GetStartupExecutablePath();
                if (string.IsNullOrWhiteSpace(exePath)) return false;

                key.SetValue(AppName, AutostartEntry.BuildCommand(exePath, startMinimized));
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public AutostartRenewal RenewOnStart()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return default;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
            if (key == null) return default;
            string? current = key.GetValue(AppName) as string;
            return AutostartEntry.Renew(current, GetStartupExecutablePath(), command => key.SetValue(AppName, command));
        }
        catch
        {
            return new AutostartRenewal(IsRunAtStartupEnabled(), false, null);
        }
    }

    public string GetStartupExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            var candidateExe = Path.ChangeExtension(path, ".exe");
            if (File.Exists(candidateExe))
            {
                return candidateExe;
            }
        }

        string? procPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(procPath) && procPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return procPath;
        }

        try
        {
            using var currentProc = Process.GetCurrentProcess();
            var mainModule = currentProc.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(mainModule))
            {
                if (string.Equals(Path.GetExtension(mainModule), ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return mainModule;
                }

                var candidateExe = Path.ChangeExtension(mainModule, ".exe");
                if (File.Exists(candidateExe))
                {
                    return candidateExe;
                }
            }
        }
        catch { }

        var baseDirExe = Path.Combine(AppContext.BaseDirectory, "MasterHub.Avalonia.exe");
        if (File.Exists(baseDirExe))
        {
            return baseDirExe;
        }

        return path ?? "";
    }
}
