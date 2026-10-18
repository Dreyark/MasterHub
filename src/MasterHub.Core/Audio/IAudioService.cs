using System.Collections.Generic;

namespace MasterHub.Core.Audio;

public interface IAudioService
{
    float GetMasterPlaybackVolume();
    void SetMasterPlaybackVolume(float volume);
    bool ToggleMasterPlaybackMute();
    bool IsMasterPlaybackMuted();

    float GetMasterRecordingVolume();
    void SetMasterRecordingVolume(float volume);
    bool ToggleMasterRecordingMute();
    bool IsMasterRecordingMuted();

    List<AudioSessionItem> GetActiveAudioSessions();
    void SetProcessVolume(string processName, float volume);
    bool ToggleProcessMute(string processName);
    bool IsProcessMuted(string processName);
}
