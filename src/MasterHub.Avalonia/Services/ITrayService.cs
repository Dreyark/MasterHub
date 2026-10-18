using System;
using Avalonia.Controls;

namespace MasterHub.Avalonia.Services;

/// <summary>
/// Serwis obsługi zasobnika systemowego (System Tray) oraz zarządzania stanem uśpienia UI.
/// </summary>
public interface ITrayService : IDisposable
{
    bool IsMinimizedToTray { get; }
    bool IsExiting { get; }

    void Initialize(Window window);
    void MinimizeToTray();
    void RestoreFromTray();
    void ExitApplication();
}
