namespace MasterHub.Core.Audio;

public enum AudioTargetType
{
    MasterPlayback,     // Główna głośność (Głośniki / Słuchawki)
    MasterRecording,    // Głośność mikrofonu
    Process,            // Konkretny proces (np. Spotify, Discord, Chrome)
    None,               // Brak powiązania
    HomeAssistantLight  // 💡 Suwak: Jasność oświetlenia Home Assistant (0..255)
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
