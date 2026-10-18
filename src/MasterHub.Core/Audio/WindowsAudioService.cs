using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NAudio.CoreAudioApi;

namespace MasterHub.Core.Audio;

/// <summary>
/// Controls Windows Master Volume, Microphone input levels, and per-process audio sessions using WASAPI / CoreAudio.
/// </summary>
public class WindowsAudioService : IAudioService
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();

    public float GetMasterPlaybackVolume()
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return dev.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return 0f;
        }
    }

    public void SetMasterPlaybackVolume(float volume)
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
        }
        catch { }
    }

    public bool ToggleMasterPlaybackMute()
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            bool newMute = !dev.AudioEndpointVolume.Mute;
            dev.AudioEndpointVolume.Mute = newMute;
            return newMute;
        }
        catch
        {
            return false;
        }
    }

    public bool IsMasterPlaybackMuted()
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return dev.AudioEndpointVolume.Mute;
        }
        catch
        {
            return false;
        }
    }

    public float GetMasterRecordingVolume()
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return dev.AudioEndpointVolume.MasterVolumeLevelScalar;
        }
        catch
        {
            return 0f;
        }
    }

    public void SetMasterRecordingVolume(float volume)
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f);
        }
        catch { }
    }

    public bool ToggleMasterRecordingMute()
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            bool newMute = !dev.AudioEndpointVolume.Mute;
            dev.AudioEndpointVolume.Mute = newMute;
            return newMute;
        }
        catch
        {
            return false;
        }
    }

    public bool IsMasterRecordingMuted()
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return dev.AudioEndpointVolume.Mute;
        }
        catch
        {
            return false;
        }
    }

    public List<AudioSessionItem> GetActiveAudioSessions()
    {
        var list = new List<AudioSessionItem>();

        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = dev.AudioSessionManager;
            sessionManager.RefreshSessions();
            var sessions = sessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                int pid = (int)s.GetProcessID;
                if (pid <= 0) continue;

                string procName = "";
                string dispName = s.DisplayName;

                try
                {
                    var proc = Process.GetProcessById(pid);
                    procName = proc.ProcessName;
                    if (string.IsNullOrWhiteSpace(dispName))
                    {
                        dispName = proc.MainWindowTitle;
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(procName)) continue;

                list.Add(new AudioSessionItem
                {
                    ProcessId = pid,
                    ProcessName = procName,
                    DisplayName = string.IsNullOrWhiteSpace(dispName) ? procName : dispName,
                    Volume = s.SimpleAudioVolume.Volume,
                    IsMuted = s.SimpleAudioVolume.Mute
                });
            }
        }
        catch { }

        return list
            .GroupBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(x => x.DisplayName)
            .ToList();
    }

    public void SetProcessVolume(string processName, float volume)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;

        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = dev.AudioSessionManager;
            sessionManager.RefreshSessions();
            var sessions = sessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                int pid = (int)s.GetProcessID;
                if (pid <= 0) continue;

                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (string.Equals(proc.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        s.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    public bool ToggleProcessMute(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = dev.AudioSessionManager;
            sessionManager.RefreshSessions();
            var sessions = sessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                int pid = (int)s.GetProcessID;
                if (pid <= 0) continue;

                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (string.Equals(proc.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        bool isMuted = !s.SimpleAudioVolume.Mute;
                        s.SimpleAudioVolume.Mute = isMuted;
                        return isMuted;
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }

    public bool IsProcessMuted(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessionManager = dev.AudioSessionManager;
            sessionManager.RefreshSessions();
            var sessions = sessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                int pid = (int)s.GetProcessID;
                if (pid <= 0) continue;

                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (string.Equals(proc.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
                    {
                        return s.SimpleAudioVolume.Mute;
                    }
                }
                catch { }
            }
        }
        catch { }

        return false;
    }
}
