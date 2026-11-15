using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MasterHub.Core.Audio;

/// <summary>An audio endpoint (speakers, headphones, microphone…).</summary>
/// <param name="Id">The endpoint id (<c>{0.0.0.00000000}.{guid}</c>) – stable until the driver is reinstalled.</param>
/// <param name="Name">Friendly name as Windows shows it, e.g. <c>2 - Realtek(R) Audio</c>.</param>
/// <param name="IsCapture">True for an input (microphone) endpoint.</param>
/// <param name="IsDefault">Whether it is the current default device of its kind.</param>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsCapture, bool IsDefault);

/// <summary>A device remembered in a key: its endpoint id <b>and</b> its name, so it can be found again when the id is gone.</summary>
public sealed record SavedAudioDevice(string Id, string Name);

/// <summary>Name handling for audio devices.</summary>
public static partial class AudioDeviceNames
{
    // Windows prefixes a duplicate friendly name with a number: "2 - Realtek(R) Audio", "3- USB Audio".
    [GeneratedRegex(@"^\s*\d+\s*-\s*")]
    private static partial Regex LeadingNumber();

    /// <summary>
    /// The name without Windows' leading device number ("2 - Realtek(R) Audio" → "Realtek(R) Audio"). The number changes when
    /// devices are plugged in a different order, so a saved device must be compared without it.
    /// </summary>
    public static string Normalize(string? name) => LeadingNumber().Replace((name ?? "").Trim(), "").Trim();

    public static bool SameName(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Finds a saved device among the devices that exist now.</summary>
public static class AudioDeviceMatcher
{
    /// <summary>By endpoint id first; when that is gone, by the normalised name. <c>null</c> when it is not present.</summary>
    public static AudioDeviceInfo? Resolve(IReadOnlyList<AudioDeviceInfo> devices, SavedAudioDevice? saved)
    {
        if (saved == null || (string.IsNullOrEmpty(saved.Id) && string.IsNullOrEmpty(saved.Name))) return null;

        var byId = devices.FirstOrDefault(d => !string.IsNullOrEmpty(saved.Id) && string.Equals(d.Id, saved.Id, StringComparison.OrdinalIgnoreCase));
        if (byId != null) return byId;

        return string.IsNullOrWhiteSpace(AudioDeviceNames.Normalize(saved.Name))
            ? null
            : devices.FirstOrDefault(d => AudioDeviceNames.SameName(d.Name, saved.Name));
    }
}

/// <summary>The action parameter of "switch audio device": two devices of one kind, <c>render;{id}|{name};{id}|{name}</c>.</summary>
public sealed record AudioSwitchParameter(bool Capture, SavedAudioDevice A, SavedAudioDevice B)
{
    public bool IsComplete => !string.IsNullOrEmpty(A.Id) && !string.IsNullOrEmpty(B.Id);

    public string Format() =>
        $"{(Capture ? "capture" : "render")};{Encode(A)};{Encode(B)}";

    public static AudioSwitchParameter Empty(bool capture = false) =>
        new(capture, new SavedAudioDevice("", ""), new SavedAudioDevice("", ""));

    /// <summary>Never throws: an unreadable parameter becomes an empty (incomplete) one.</summary>
    public static AudioSwitchParameter Parse(string? text)
    {
        var parts = (text ?? "").Split(';');
        if (parts.Length != 3 || (parts[0] != "render" && parts[0] != "capture")) return Empty();
        return new AudioSwitchParameter(parts[0] == "capture", Decode(parts[1]), Decode(parts[2]));
    }

    private static string Encode(SavedAudioDevice d) => $"{d.Id}|{Uri.EscapeDataString(d.Name)}";

    private static SavedAudioDevice Decode(string segment)
    {
        int bar = segment.IndexOf('|');
        if (bar < 0) return new SavedAudioDevice(segment, "");
        return new SavedAudioDevice(segment[..bar], Uri.UnescapeDataString(segment[(bar + 1)..]));
    }
}

/// <summary>Why a switch could not be planned.</summary>
public enum AudioSwitchProblem
{
    None,
    NotConfigured,
    FirstMissing,
    SecondMissing,
    BothMissing,
    SameDevice
}

/// <summary>What one press should do: switch to <see cref="Target"/>, or explain <see cref="Problem"/>.</summary>
public sealed record AudioSwitchPlan(AudioDeviceInfo? Target, AudioSwitchProblem Problem)
{
    public bool CanSwitch => Target != null;
}

/// <summary>The decision behind the key: pure, so it is tested with fake device lists.</summary>
public static class AudioSwitchPlanner
{
    /// <summary>
    /// "One press toggles between two devices": when the default is A the target is B, otherwise A. A device that is
    /// missing (unplugged, renamed beyond recognition) gives a clear problem instead of an exception; when only one of the
    /// two is present the press switches to it (if it is not already the default).
    /// </summary>
    public static AudioSwitchPlan Plan(IReadOnlyList<AudioDeviceInfo> devices, AudioSwitchParameter parameter)
    {
        if (!parameter.IsComplete) return new(null, AudioSwitchProblem.NotConfigured);

        var kind = devices.Where(d => d.IsCapture == parameter.Capture).ToList();
        var a = AudioDeviceMatcher.Resolve(kind, parameter.A);
        var b = AudioDeviceMatcher.Resolve(kind, parameter.B);

        if (a == null && b == null) return new(null, AudioSwitchProblem.BothMissing);
        if (a != null && b != null && string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase))
            return new(null, AudioSwitchProblem.SameDevice);
        if (a == null) return b!.IsDefault ? new(null, AudioSwitchProblem.FirstMissing) : new(b, AudioSwitchProblem.FirstMissing);
        if (b == null) return a.IsDefault ? new(null, AudioSwitchProblem.SecondMissing) : new(a, AudioSwitchProblem.SecondMissing);

        return new(a.IsDefault ? b : a, AudioSwitchProblem.None);
    }
}

/// <summary>Lists audio endpoints and changes the Windows default device.</summary>
public interface IAudioDeviceService : IDisposable
{
    /// <summary>Active endpoints of one kind (<c>capture</c> = microphones, otherwise playback devices).</summary>
    IReadOnlyList<AudioDeviceInfo> GetDevices(bool capture);

    /// <summary>Every active endpoint (playback and capture).</summary>
    IReadOnlyList<AudioDeviceInfo> GetAllDevices();

    /// <summary>
    /// Makes an endpoint the default one for every role (console, multimedia, communications). Windows has no public API for
    /// this; it goes through the undocumented <c>IPolicyConfig</c> interface (as SoundSwitch, EarTrumpet and NirCmd do).
    /// Returns <c>false</c> with a reason when it did not work.
    /// </summary>
    bool SetDefault(string deviceId, out string error);

    /// <summary>Raised when the default playback or recording device changed (also by other programs).</summary>
    event Action? DefaultDeviceChanged;
}
