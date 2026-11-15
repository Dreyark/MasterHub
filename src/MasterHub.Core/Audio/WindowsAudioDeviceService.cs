using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;


namespace MasterHub.Core.Audio;

/// <summary>
/// Windows implementation of <see cref="IAudioDeviceService"/>. Enumeration uses NAudio; changing the default device needs the
/// undocumented <c>IPolicyConfig</c> COM interface, which is isolated in this class (and wrapped in try/catch) so that a
/// Windows update that changes it can never crash the app – the key simply reports that the switch failed.
/// </summary>
public sealed class WindowsAudioDeviceService : IAudioDeviceService
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private MMDeviceNotificationClient? _notifications;
    private bool _disposed;

    public WindowsAudioDeviceService()
    {
        try
        {
            // Events arrive on a Windows audio thread: do not marshal to a UI context, just tell listeners something changed.
            _notifications = _enumerator.CreateNotificationClient(useSynchronizationContext: false);
            _notifications.DefaultDeviceChanged += (_, _) => RaiseChanged();
            _notifications.DeviceStateChanged += (_, _) => RaiseChanged();
            _notifications.DeviceAdded += (_, _) => RaiseChanged();
            _notifications.DeviceRemoved += (_, _) => RaiseChanged();
        }
        catch { /* notifications are optional */ }
    }

    public event Action? DefaultDeviceChanged;

    public IReadOnlyList<AudioDeviceInfo> GetDevices(bool capture) => GetAllDevices().Where(d => d.IsCapture == capture).ToList();

    public IReadOnlyList<AudioDeviceInfo> GetAllDevices()
    {
        var result = new List<AudioDeviceInfo>();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            try
            {
                string? defaultId = null;
                try
                {
                    using var def = _enumerator.GetDefaultAudioEndpoint(flow, Role.Multimedia);
                    defaultId = def.ID;
                }
                catch { /* no default device of this kind */ }

                foreach (var device in _enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
                {
                    using (device)
                    {
                        result.Add(new AudioDeviceInfo(device.ID, device.FriendlyName, flow == DataFlow.Capture,
                            string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase)));
                    }
                }
            }
            catch
            {
                // enumeration failed for this kind: report what we have
            }
        }
        return result;
    }

    public bool SetDefault(string deviceId, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            error = "no device";
            return false;
        }

        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            try
            {
                // Console, Multimedia, Communications: the same device for every kind of program.
                foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                {
                    int hr = policy.SetDefaultEndpoint(deviceId, role);
                    if (hr != 0)
                    {
                        error = $"SetDefaultEndpoint failed (0x{hr:X8})";
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }


    private void RaiseChanged()
    {
        try { DefaultDeviceChanged?.Invoke(); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _notifications?.Dispose(); } catch { }
        _enumerator.Dispose();
    }

    // ── the undocumented interface ─────────────────────────────────────────────────────────

    private enum ERole
    {
        Console = 0,
        Multimedia = 1,
        Communications = 2
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient
    {
    }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        // The first ten methods are never called; they only keep the vtable layout right.
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool defaultPeriod, IntPtr defaultPeriodValue, IntPtr minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }
}
