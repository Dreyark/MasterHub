namespace MasterHub.Core.Audio;

public enum AudioTargetType
{
    MasterPlayback,     // Główna głośność (Głośniki / Słuchawki)
    MasterRecording,    // Głośność mikrofonu
    Process,            // Konkretny proces (np. Spotify, Discord, Chrome)
    None,               // Brak powiązania
    HomeAssistantLight, // 💡 Suwak: Jasność oświetlenia Home Assistant (0..255)
    ForegroundApp,      // 🔊 Program na wierzchu (T-17 d)
    EverythingElse      // 🎚 Wszystko inne – programy, których nie obsługuje żaden inny suwak profilu (T-17 h)
}

public class AudioSessionItem
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public float Volume { get; set; }
    public bool IsMuted { get; set; }

    public override string ToString() => string.IsNullOrWhiteSpace(DisplayName) ? ProcessName : $"{DisplayName} ({ProcessName})";
}
