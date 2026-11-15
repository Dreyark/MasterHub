namespace MasterHub.Core.SystemControl;

/// <summary>
/// Serwis zarządzania autostartem aplikacji przy starcie systemu operacyjnego.
/// </summary>
public interface IStartupService
{
    /// <summary>Sprawdza, czy aplikacja jest zarejestrowana do uruchamiania przy starcie Windows.</summary>
    bool IsRunAtStartupEnabled();

    /// <summary>Włącza lub wyłącza uruchamianie aplikacji przy starcie Windows.</summary>
    bool SetRunAtStartup(bool enable, bool startMinimized = true);

    /// <summary>Pobiera ścieżkę do pliku wykonywalnego aplikacji do wpisu autostartu.</summary>
    string GetStartupExecutablePath();

    /// <summary>
    /// Called once at every start (T-34 #4): an existing autostart entry is rewritten for the current program path (keeping
    /// "start minimised"), so a moved or updated MasterHub still starts with Windows. Writes nothing when autostart is off.
    /// </summary>
    AutostartRenewal RenewOnStart() => new(IsRunAtStartupEnabled(), false, null);
}
