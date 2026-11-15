using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>An entry of the "when pressed again" list of a sound key.</summary>
public sealed record SoundModeOption(SoundPlayMode Mode, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Edits the <see cref="SoundSettings"/> of one sound key (the inspector section binds to it).</summary>
public sealed partial class SoundEditor : ObservableObject
{
    private readonly KeyItemViewModel _key;

    public SoundEditor(KeyItemViewModel key) => _key = key;

    private SoundSettings Model => _key.SoundSettings;

    public string FilePath
    {
        get => Model.FilePath;
        set
        {
            Model.FilePath = value?.Trim() ?? "";
            _key.NameFromSoundFile();
            Touch(nameof(FilePath), nameof(FileName), nameof(ProblemText), nameof(HasProblem));
        }
    }

    public string FileName => Path.GetFileName(Model.FilePath);

    public int VolumePercent
    {
        get => Model.VolumePercent;
        set { Model.VolumePercent = Math.Clamp(value, 0, SoundSettings.MaxVolumePercent); Touch(nameof(VolumePercent)); }
    }

    public IReadOnlyList<SoundModeOption> Modes => KeyItemViewModel.SoundModeOptions;

    public SoundModeOption SelectedMode
    {
        get => Modes.First(m => m.Mode == Model.Mode);
        set { Model.Mode = value.Mode; Touch(nameof(SelectedMode)); }
    }

    /// <summary>The Windows default device first, then every playback device that exists now (a virtual cable included).</summary>
    public IReadOnlyList<AudioDeviceOption> Devices
    {
        get
        {
            var list = new List<AudioDeviceOption> { new("", LocalizationService.Instance.GetString("Sound_DefaultDevice", "Default device")) };
            list.AddRange(_key.PlaybackDevices);

            // a saved device that is not connected stays visible instead of silently turning into "default"
            if (!Model.UsesDefaultDevice && !list.Any(d => d.Id == Model.DeviceId || AudioDeviceNames.SameName(d.Name, Model.DeviceName)))
                list.Add(new AudioDeviceOption(Model.DeviceId, Model.DeviceName));
            return list;
        }
    }

    public AudioDeviceOption SelectedDevice
    {
        get
        {
            var devices = Devices;
            if (Model.UsesDefaultDevice) return devices[0];

            var match = AudioDeviceMatcher.Resolve(
                devices.Skip(1).Select(d => new AudioDeviceInfo(d.Id, d.Name, false, false)).ToList(), new SavedAudioDevice(Model.DeviceId, Model.DeviceName));
            return match == null ? devices[^1] : devices.First(d => d.Id == match.Id);
        }
        set
        {
            if (value == null) return;
            Model.DeviceId = value.Id;
            Model.DeviceName = value.Id.Length == 0 ? "" : value.Name;
            Touch(nameof(SelectedDevice), nameof(ProblemText), nameof(HasProblem));
        }
    }

    /// <summary>What is wrong with the key (shown before it is ever pressed); empty when it is fine.</summary>
    public string ProblemText => SoundMessages.For(SoundFiles.Check(Model.FilePath), Model.FilePath, Model.DeviceName, MissingDevice);

    public bool HasProblem => ProblemText.Length > 0;

    private bool MissingDevice =>
        !Model.UsesDefaultDevice &&
        AudioDeviceMatcher.Resolve(_key.PlaybackDevices.Select(d => new AudioDeviceInfo(d.Id, d.Name, false, false)).ToList(),
            new SavedAudioDevice(Model.DeviceId, Model.DeviceName)) == null;

    private void Touch(params string[] names)
    {
        foreach (string n in names) OnPropertyChanged(n);
        _key.NotifySoundSettingsChanged();
    }
}

/// <summary>The words for what can go wrong with a sound (shared by the inspector and the red-corner reason).</summary>
public static class SoundMessages
{
    public static string For(SoundProblem problem, string file, string device, bool deviceMissing = false)
    {
        var loc = LocalizationService.Instance;
        if (problem == SoundProblem.None && deviceMissing) problem = SoundProblem.DeviceMissing;

        return problem switch
        {
            SoundProblem.NoFile => loc.GetString("Sound_NoFile", "No sound file chosen yet."),
            SoundProblem.UnsupportedType => loc.GetString("Sound_Unsupported", "This file type is not supported. Use WAV, MP3, M4A, AAC, WMA or FLAC."),
            SoundProblem.FileMissing => string.Format(loc.GetString("Sound_Missing", "The sound file was not found: {0}"), file),
            SoundProblem.DeviceMissing => string.Format(loc.GetString("Sound_DeviceMissing", "The output device \"{0}\" is not connected."), AudioDeviceNames.Normalize(device)),
            SoundProblem.PlaybackFailed => loc.GetString("Sound_Failed", "The sound could not be played."),
            _ => ""
        };
    }
}

/// <summary>Sound key (T-14): plays a sound file on a chosen playback device, also a virtual cable for Discord / OBS.</summary>
public partial class KeyItemViewModel
{
    private SoundEditor? _soundEditor;

    /// <summary>The key's sound set-up (always present; only saved for a <see cref="DeckActionType.SoundPlay"/> key).</summary>
    public SoundSettings SoundSettings { get; private set; } = new();

    public SoundEditor SoundEditor => _soundEditor ??= new SoundEditor(this);

    public bool IsSoundAction => AssignedAction == DeckActionType.SoundPlay;

    public static IReadOnlyList<SoundModeOption> SoundModeOptions
    {
        get
        {
            var loc = LocalizationService.Instance;
            return new SoundModeOption[]
            {
                new(SoundPlayMode.PlayStop, loc.GetString("Sound_ModePlayStop", "Play / stop")),
                new(SoundPlayMode.Restart, loc.GetString("Sound_ModeRestart", "Restart")),
                new(SoundPlayMode.Overlap, loc.GetString("Sound_ModeOverlap", "Overlap"))
            };
        }
    }

    /// <summary>Playback devices that exist now.</summary>
    internal IReadOnlyList<AudioDeviceOption> PlaybackDevices =>
        (GetService<IAudioDeviceService>()?.GetDevices(capture: false) ?? new List<AudioDeviceInfo>())
        .Select(d => new AudioDeviceOption(d.Id, d.Name)).ToList();

    /// <summary>A chosen file names the key while it still has the automatic title (a label the user typed is never replaced).</summary>
    internal void NameFromSoundFile()
    {
        if (!IsSoundAction) return;
        if (!string.IsNullOrEmpty(CustomTitle) && CustomTitle != GetOriginalActionTitle() && CustomTitle != _soundAutoTitle) return;

        string name = Path.GetFileNameWithoutExtension(SoundSettings.FilePath);
        if (name.Length > 12) name = name[..11] + "…";
        _soundAutoTitle = name;
        CustomTitle = name.Length > 0 ? name : GetOriginalActionTitle();
    }

    private string _soundAutoTitle = "";

    internal void NotifySoundSettingsChanged()
    {
        OnPropertyChanged(nameof(SoundSettings));
        ActionChangedCallback?.Invoke(this);
    }

    public void LoadSound(SoundSettings? model)
    {
        SoundSettings = (model ?? new SoundSettings()).Normalize();
        _soundEditor = null;
        OnPropertyChanged(nameof(SoundSettings));
        OnPropertyChanged(nameof(SoundEditor));
    }

    /// <summary>The set-up to save, or <c>null</c> for a key that is not a sound key.</summary>
    public SoundSettings? ToSoundModel() => IsSoundAction ? SoundSettings : null;

    private void ResetSound()
    {
        SoundSettings = new SoundSettings();
        _soundEditor = null;
    }
}
