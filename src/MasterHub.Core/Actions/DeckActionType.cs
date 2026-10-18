namespace MasterHub.Core.Actions;

public enum DeckActionType
{
    None = 0,           // Pusty / Nieprzypisany (Czysty ekran)
    DefaultNumber = 1,  // Domyślny numer przycisku (BTN 1..15)
    MediaPlayPause = 2, // ⏯️ Play / Pause
    MediaNext = 3,      // ⏭️ Następny utwór
    MediaPrevious = 4,  // ⏮️ Poprzedni utwór
    MediaStop = 5,      // ⏹️ Zatrzymaj utwór
    MasterMute = 6,     // 🔇 Wycisz / Odcisz Głośniki
    MicMute = 7,        // 🎙️ Wycisz / Odcisz Mikrofon
    VolumeUp = 8,       // 🔊 Głośniej (+2%)
    VolumeDown = 9,     // 🔉 Ciszej (-2%)
    Screenshot = 10,    // 📸 Zrzut ekranu (Win+Shift+S)
    TaskManager = 11,   // 📊 Menedżer zadań
    Calculator = 12,    // 🧮 Kalkulator
    Browser = 13,       // 🌐 Przeglądarka

    // --- ETAP 1: URUCHAMIANIE APLIKACJI I PLIKÓW ---
    OpenApplication = 20,   // 🚀 Uruchom aplikację (.exe / .lnk / ścieżka)
    OpenWebsite = 21,       // 🌐 Otwórz URL w domyślnej przeglądarce
    OpenFile = 22,          // 📂 Otwórz plik lub folder w eksploratorze
    CloseApplication = 23,  // ❌ Zamknij aktywną lub wskazaną aplikację

    // --- ETAP 1: TEKST, SKRÓTY I SEKWENCJE ---
    TextPaste = 30,         // 📋 Wklej predefiniowany tekst (SendKeys/Clipboard)
    Hotkey = 31,            // ⌨️ Dowolny skrót klawiaturowy (Ctrl+Shift+S itp.)
    HotkeySwitch = 32,      // 🔁 Przełącznik między dwoma skrótami (Toggle on/off)
    MultiAction = 35,       // ⚡ Sekwencja wielu akcji z opóźnieniami
    Delay = 36,             // ⏳ Pauza / opóźnienie w sekwencji (ms)

    // --- ETAP 1: ZASILANIE I BLOKADA SYSTEMU ---
    SystemSleep = 40,       // 💤 Uśpij komputer
    SystemShutdown = 41,    // ⏻ Wyłącz komputer
    SystemRestart = 42,     // 🔄 Uruchom ponownie komputer
    SystemLockScreen = 43,  // 🔒 Zablokuj ekran (Win+L)
    SystemScreensaver = 44, // 🖥️ Uruchom wygaszacz ekranu

    // --- BEZPOŚREDNIA KONTROLA MYSZKI RAZER (NATIVE HARDWARE HID) ---
    RazerDpiCycle = 50,              // 🎯 Cykl etapów DPI (400 -> 800 -> 1600 -> 3200 -> 6400)
    RazerDpiUp = 51,                 // 🎯 DPI w górę (kolejny poziom DPI)
    RazerDpiDown = 52,               // 🎯 DPI w dół (poprzedni poziom DPI)
    RazerProfileNext = 53,           // 🐍 Następny profil myszy
    RazerProfilePrev = 54,           // 🐍 Poprzedni profil myszy
    RazerProfile1 = 55,              // 🐍 Mysz: Profil 1
    RazerProfile2 = 56,              // 🐍 Mysz: Profil 2
    RazerProfile3 = 57,              // 🐍 Mysz: Profil 3
    RazerProfile4 = 58,              // 🐍 Mysz: Profil 4
    RazerProfile5 = 59,              // 🐍 Mysz: Profil 5

    // --- CZĘSTOTLIWOŚĆ PRÓBKOWANIA MYSZKI (POLLING RATE HZ) ---
    RazerPollingRateCycle = 60,      // ⚡ Cykl odświeżania (125 -> 500 -> 1000 -> 2000 -> 4000 -> 8000 Hz)
    RazerPollingRateUp = 61,         // ⚡ Częstotliwość w górę
    RazerPollingRateDown = 62,       // ⚡ Częstotliwość w dół
    RazerPollingRate125 = 63,        // ⚡ Mysz: 125 Hz
    RazerPollingRate500 = 64,        // ⚡ Mysz: 500 Hz
    RazerPollingRate1000 = 65,       // ⚡ Mysz: 1000 Hz
    RazerPollingRate2000 = 66,       // ⚡ Mysz: 2000 Hz
    RazerPollingRate4000 = 67,       // ⚡ Mysz: 4000 Hz
    RazerPollingRate8000 = 68,       // ⚡ Mysz: 8000 Hz

    // --- KONTROLA KLAWIATURY WOOTING (NATIVE HID) ---
    WootingProfileCycle = 80,        // ⌨️ Cykl profili klawiatury (Cyfrowy -> Analog 1 -> 2 -> 3)
    WootingProfileNext = 81,         // ⌨️ Następny profil klawiatury
    WootingProfilePrev = 82,         // ⌨️ Poprzedni profil klawiatury
    WootingProfileDigital = 83,      // ⌨️ Profil 0: Cyfrowy (Digital)
    WootingProfile1 = 84,            // ⌨️ Profil 1: Analogowy #1
    WootingProfile2 = 85,            // ⌨️ Profil 2: Analogowy #2
    WootingProfile3 = 86,            // ⌨️ Profil 3: Analogowy #3

    // --- BEZPOŚREDNIE POZIOMY DPI MYSZKI RAZER ---
    RazerDpi400 = 72,                // 🎯 Razer DPI: 400
    RazerDpi800 = 73,                // 🎯 Razer DPI: 800
    RazerDpi1600 = 74,               // 🎯 Razer DPI: 1600
    RazerDpi3200 = 75,               // 🎯 Razer DPI: 3200
    RazerDpi6400 = 76,               // 🎯 Razer DPI: 6400

    // --- ETAP 2: PROFILES, STRONY I NAWIGACJA FOLDERÓW ---
    GoToPage = 90,                   // 📄 Przejdź do strony N
    GoToProfile = 91,                // 🔀 Przełącz na profil X
    OpenFolder = 92,                 // 📁 Wejdź do folderu
    GoBack = 93,                     // ⬅️ Wróć do poprzedniego widoku
    GoHome = 94,                     // 🏠 Wróć do strony głównej profilu
    NextPage = 95,                   // ⏭️ Następna strona
    PreviousPage = 96,               // ⏮️ Poprzednia strona

    // --- ETAP 3: LIVE DATA & SYSTEM MONITORING NA LCD ---
    DisplayCpuUsage = 100,           // 📊 Wyświetl zużycie CPU (%)
    DisplayRamUsage = 101,           // 🧠 Wyświetl zużycie RAM (GB / %)
    DisplayGpuUsage = 102,           // 🎮 Wyświetl zużycie GPU (%)
    DisplayCpuTemp = 103,            // 🌡️ Wyświetl temperaturę CPU (°C)
    DisplayGpuTemp = 104,            // 🌡️ Wyświetl temperaturę GPU (°C)
    DisplayNetworkSpeed = 105,       // 📡 Wyświetl prędkość sieci (Mb/s)
    DisplayDiskUsage = 106,          // 💾 Wyświetl użycie dysku (%)
    DisplayClock = 107,              // 🕐 Wyświetl aktualny czas (HH:mm)
    DisplayDate = 108,               // 📅 Wyświetl aktualną datę (DD MMM)
    NowPlaying = 109,                // 🎵 Teraz odtwarzane (Now Playing)
    DisplayFps = 110,                // 🎯 Licznik FPS (Klatki na sekundę)

    // --- ETAP 4: STREAMING — OBS STUDIO ---
    ObsToggleStream = 120,           // 📡 Przełącz stream (Start / Stop)
    ObsStartStream = 121,            // ▶️ Rozpocznij stream
    ObsStopStream = 122,             // ⏹️ Zatrzymaj stream
    ObsToggleRecord = 123,           // 🔴 Przełącz nagrywanie (Start / Stop)
    ObsStartRecord = 124,            // ▶️ Rozpocznij nagrywanie
    ObsStopRecord = 125,             // ⏹️ Zatrzymaj nagrywanie
    ObsToggleVirtualCam = 126,       // 📹 Przełącz wirtualną kamerę (Start / Stop)
    ObsSaveReplayBuffer = 127,       // 💾 Zapisz bufor powtórek (Instant Replay)
    ObsSceneSwitch = 128,            // 🎬 Przełącz scenę OBS (SetCurrentProgramScene)
    ObsStudioModeToggle = 129,       // 🎭 Przełącz tryb studyjny OBS (Studio Mode Toggle)
    ObsTriggerTransition = 130,      // 🔀 Wyzwól przejście scen w trybie studyjnym (TriggerStudioModeTransition)

    // --- ETAP 4.6 / 4.7: STREAMING — TWITCH CHAT ---
    TwitchChatEmoteOnly = 135,       // 😀 Przełącz tryb tylko dla emotek (Emote-Only)
    TwitchChatSlowMode = 136,        // 🐢 Przełącz tryb powolny czatu (Slow Mode)
    TwitchChatSubOnly = 137,         // ⭐ Przełącz tryb tylko dla subskrybentów (Sub-Only)
    TwitchClearChat = 138,           // 🧹 Wyczyść czat transmisji (/clear)
    TwitchSendMessage = 139,         // 💬 Wyślij predefiniowaną wiadomość na czat

    // --- ETAP 4.7: STREAMING — TWITCH CLIPS & MARKERS ---
    TwitchCreateClip = 140,          // 🎬 Utwórz klip z transmisji na żywo (Twitch Helix API)
    TwitchCreateMarker = 141,        // 📍 Utwórz znacznik w transmisji na żywo (Stream Marker)

    // --- ETAP 4.8: STREAMING — TWITCH RAIDS & COMMERCIALS ---
    TwitchStartRaid = 142,           // 🚀 Rozpocznij Raid do wskazanego kanału (Twitch Helix API)
    TwitchCancelRaid = 143,          // 🛑 Anuluj trwający Raid (Twitch Helix API)
    TwitchRunCommercial = 144,       // 📺 Uruchom przerwę reklamową Commercial (Twitch Helix API)

    // --- ETAP 4.9: STREAMING — YOUTUBE LIVE ---
    YouTubeLiveViewers = 145,        // ▶️ Status transmisji na żywo & licznik widzów YouTube Live

    // --- ETAP 4.10: STREAMING — STREAMLABS DESKTOP ---
    StreamlabsSceneSwitch = 148,     // 🎥 Przełączanie sceny w Streamlabs Desktop (port 59650 / Named Pipe)

    // --- ETAP 4.11: STREAMING — KICK ---
    KickChatEmoteOnly = 170,       // 😀 Kick: Przełącz tryb tylko dla emotek (Emote-Only)
    KickChatSlowMode = 171,        // 🐢 Kick: Przełącz tryb powolny czatu (Slow Mode)
    KickChatSubOnly = 172,         // ⭐ Kick: Przełącz tryb tylko dla subów (Sub-Only)
    KickClearChat = 173,           // 🧹 Kick: Wyczyść czat (/clear)
    KickSendMessage = 174,         // 💬 Kick: Wyślij wiadomość na czat
    KickCreateClip = 175,          // 🎬 Kick: Utwórz klip z transmisji

    // --- ETAP 5.2: KOMUNIKATORY — DISCORD (MUTE / DEAFEN) ---
    DiscordMuteToggle = 150,         // 🎙️ Wycisz / odcisz mikrofon na Discordzie (Mute Toggle)
    DiscordDeafenToggle = 151,       // 🎧 Wycisz / odcisz odsłuch na Discordzie (Deafen Toggle)

    // --- ETAP 5.3: KOMUNIKATORY — DISCORD (KAMERA & EKRAN) ---
    DiscordCameraToggle = 152,       // 📷 Włącz / wyłącz kamerę w rozmowie Discord
    DiscordScreenShareToggle = 153,  // 🖥️ Włącz / wyłącz udostępnianie ekranu (Go Live) na Discordzie

    // --- ETAP 5.4: KOMUNIKATORY — DISCORD (PUSH-TO-TALK) ---
    DiscordPushToTalk = 154,         // 🎙️ Push-To-Talk (Naciśnij i mów / OnHold) na Discordzie

    // --- ETAP 5.5: KOMUNIKATORY — DISCORD (DOŁĄCZANIE DO KANAŁU) ---
    DiscordJoinVoiceChannel = 155,   // 🔊 Dołącz do wskazanego kanału głosowego Discord (Voice Channel)

    // --- ETAP 5.6: KOMUNIKATORY — MICROSOFT TEAMS ---
    TeamsMuteToggle = 160,           // 🎙️ Wycisz / odcisz mikrofon w MS Teams (Ctrl+Shift+M)
    TeamsCameraToggle = 161,         // 📷 Włącz / wyłącz kamerę w MS Teams (Ctrl+Shift+O)
    TeamsRaiseHand = 162,            // ✋ Podnieś / opuść rękę w MS Teams (Ctrl+Shift+K)
    TeamsLeaveMeeting = 163,         // 📞 Opuść spotkanie w MS Teams (Ctrl+Shift+H)

    // --- ETAP 6.1 & 6.2: MULTIMEDIA — SPOTIFY ---
    SpotifyToggleShuffle = 185,      // 🔀 Spotify: Przełącz losowe odtwarzanie (Shuffle)
    SpotifyToggleRepeat = 186,       // 🔁 Spotify: Przełącz powtarzanie (Off -> Context -> Track)
    SpotifyLikeTrack = 187,          // 💚 Spotify: Polub / Odpolub bieżący utwór (Save to Liked Songs)
    SpotifyAddToQueue = 188,         // ➕ Spotify: Dodaj utwór do kolejki (Add to Queue)
    SpotifyPlayUri = 189,            // ▶️ Spotify: Uruchom playlistę / album / utwór po URI (Play URI)

    // --- ETAP 6.4 & 6.5: MULTIMEDIA — VOICEMOD ---
    VoicemodVoiceSelect = 220,     // 🤖 Voicemod: Wybierz głos (Load Voice wg ID/nazwy)
    VoicemodVoiceToggle = 221,     // 🎙️ Voicemod: Włącz / Wyłącz modulację głosu (Voice Changer On/Off)
    VoicemodMuteVoice = 222,       // 🔇 Voicemod: Wycisz / Odcisz mikrofon (Mute Mic Toggle)
    VoicemodHearMyVoiceToggle = 223, // 🎧 Voicemod: Odsłuch własnego głosu (Hear Myself Toggle)
    VoicemodBackgroundToggle = 226,  // 🎧 Voicemod: Dźwięk tła / Background FX (Toggle)
    VoicemodPlaySound = 224,       // 🔊 Voicemod: Odtwórz dźwięk / mem z Soundboardu (Play Sound/Meme)
    VoicemodStopAllSounds = 225,   // ⏹️ Voicemod: Zatrzymaj wszystkie dźwięki Soundboardu (Stop All Sounds)

    // --- ETAP 6: ZARZĄDZANIE SERWEREM — JETKVM ATX ---
    JetKvmPowerShort = 180,        // 🔌 JetKVM: Włącz / ACPI Wyłącz (Krótki impuls)
    JetKvmPowerLong = 181,         // ⏻ JetKVM: Wymuszone wyłączenie (5s)
    JetKvmReset = 182,             // 🔄 JetKVM: Reset sprzętowy
    JetKvmPowerStatus = 183,       // 💡 JetKVM: Monitor zasilania (Stan Power LED)

    // --- ETAP 6.3: MULTIMEDIA — VLC MEDIA PLAYER ---
    VlcPlayPause = 190,            // ⏯️ VLC: Odtwarzaj / Wstrzymaj (Play / Pause Toggle)
    VlcStop = 191,                 // ⏹️ VLC: Zatrzymaj odtwarzanie (Stop)
    VlcNext = 192,                 // ⏭️ VLC: Następny utwór / rozdział (Next)
    VlcPrevious = 193,             // ⏮️ VLC: Poprzedni utwór / rozdział (Previous)
    VlcFullscreenToggle = 194,     // ⛶ VLC: Przełącz pełny ekran (Fullscreen Toggle)

    // Zachowane dla wstecznej kompatybilności zapisanych profili (nie używane)
    WindowsSensitivityUp = 198,
    WindowsSensitivityDown = 199,
    RazerShortcutNextProfile = 200,
    RazerShortcutPrevProfile = 201,
    RazerShortcutProfile1 = 202,
    RazerShortcutProfile2 = 203,
    RazerShortcutProfile3 = 204,
    RazerShortcutProfile4 = 205,
    RazerShortcutProfile5 = 206,
    RazerShortcutDpiUp = 207,
    RazerShortcutDpiDown = 208,
    RazerShortcutDpiCycle = 209,
    RazerApiNextProfile = 210,
    RazerApiPrevProfile = 211,
    RazerApiSelectProfile = 212,

    // --- ETAP 7.2: SYSTEM WINDOWS & PULPIT — WIRTUALNE PULPITY ---
    SystemVirtualDesktopNext = 231,   // 🗔 Następny wirtualny pulpit (Win+Ctrl+Right)
    SystemVirtualDesktopPrev = 232,   // 🖥️ Poprzedni wirtualny pulpit (Win+Ctrl+Left)
    SystemVirtualDesktopCreate = 233, // ➕ Nowy wirtualny pulpit (Win+Ctrl+D)
    SystemVirtualDesktopClose = 234,  // ✖️ Zamknij bieżący wirtualny pulpit (Win+Ctrl+F4)

    // --- ETAP 7.3: SYSTEM WINDOWS & PULPIT — WIDOK ZADAŃ & MINIMALIZACJA ---
    SystemTaskView = 235,             // 🗂️ Widok zadań Windows (Win+Tab)
    SystemShowDesktop = 236,          // 🖥️ Pokaż pulpit / Minimalizuj okna (Win+D)

    // --- ETAP 7.4: SYSTEM WINDOWS & PULPIT — HISTORIA SCHOWKA ---
    SystemClipboardHistory = 237,     // 📋 Historia schowka Windows (Win+V)

    // --- ETAP 8.2: AKCJE WARUNKOWE (CONDITIONAL ACTIONS) ---
    ConditionalAction = 238           // 🔀 Akcja warunkowa (IF / THEN / ELSE)
}

public enum RollerActionType
{
    None = 0,               // Pusta / Nieprzypisana
    MasterVolume = 1,       // Regulacja głównej głośności (▲ Głośniej / ▼ Ciszej)
    ScrollVertical = 2,     // Kółko myszy (Przewijanie góra / dół)
    MediaTrack = 3,         // Następny / Poprzedni utwór
    ScrollHorizontal = 4,   // Przewijanie poziome
    RazerDpiCycle = 5,      // 🎯 Rolka: Regulacja DPI myszki Razer (▲ DPI+ / ▼ DPI-)
    RazerProfileCycle = 6,  // 🐍 Rolka: Przełączanie profili myszki Razer (▲ Następny / ▼ Poprzedni)
    RazerPollingRateCycle = 7, // ⚡ Rolka: Zmiana częstotliwości Hz myszki (▲ Wyższa / ▼ Niższa)
    WootingProfileCycle = 8,   // ⌨️ Rolka: Przełączanie profili klawiatury Wooting (▲ Następny / ▼ Poprzedni)
    WindowsSensitivity = 99    // Zachowane dla kompatybilności wstecznej
}
