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
}
