namespace MasterHub.Core.SystemControl;

public interface ISystemControlService
{
    // Multimedia
    void PlayPause();
    void NextTrack();
    void PreviousTrack();
    void StopTrack();
    void VolumeUp();
    void VolumeDown();
    void MuteVolume();

    // Mysz (scroll)
    void ScrollVertical(int clicks);
    void ScrollHorizontal(int clicks);

    // System (istniejące)
    void TakeScreenshot();
    void OpenTaskManager();
    void OpenCalculator();
    void OpenDefaultBrowser();
    void LaunchProcess(string pathOrCommand);

    // --- ETAP 1: NOWE METODY ---

    /// <summary>Uruchamia aplikację lub otwiera plik (.exe, .lnk, URL, ścieżka folderu)</summary>
    void OpenApplication(string path);

    /// <summary>Otwiera URL w domyślnej przeglądarce</summary>
    void OpenWebsite(string url);

    /// <summary>Otwiera plik lub folder w eksploratorze Windows</summary>
    void OpenFile(string path);

    /// <summary>Zamyka wskazaną aplikację (lub okno na pierwszym planie, jeśli parametr jest pusty)</summary>
    void CloseApplication(string processNameOrTitle = "");

    /// <summary>Wkleja tekst do aktywnego okna przez schowek</summary>
    void PasteText(string text);

    /// <summary>Wysyła kombinację klawiszy (np. "Ctrl+Shift+S")</summary>
    void SendHotkey(string hotkeyString);

    /// <summary>Uśpia komputer (Sleep/Hibernate)</summary>
    void SystemSleep();

    /// <summary>Wyłącza komputer</summary>
    void SystemShutdown();

    /// <summary>Restartuje komputer</summary>
    void SystemRestart();

    /// <summary>Blokuje ekran (Win+L)</summary>
    void SystemLockScreen();

    /// <summary>Uruchamia wygaszacz ekranu</summary>
    void SystemScreensaver();

    // --- ETAP 7.2: WIRTUALNE PULPITY ---
    /// <summary>Przełącza na następny wirtualny pulpit (Win+Ctrl+Right)</summary>
    void VirtualDesktopNext();

    /// <summary>Przełącza na poprzedni wirtualny pulpit (Win+Ctrl+Left)</summary>
    void VirtualDesktopPrev();

    /// <summary>Tworzy nowy wirtualny pulpit (Win+Ctrl+D)</summary>
    void VirtualDesktopCreate();

    /// <summary>Zamyka bieżący wirtualny pulpit (Win+Ctrl+F4)</summary>
    void VirtualDesktopClose();

    // --- ETAP 7.3: WIDOK ZADAŃ & MINIMALIZACJA ---
    /// <summary>Otwiera Widok Zadań (Task View: Win+Tab)</summary>
    void TaskView();

    /// <summary>Pokazuje pulpit / minimalizuje i przywraca wszystkie okna (Win+D)</summary>
    void ShowDesktop();

    // --- ETAP 7.4: HISTORIA SCHOWKA ---
    /// <summary>Otwiera historię schowka Windows (Win+V)</summary>
    void OpenClipboardHistory();
}
