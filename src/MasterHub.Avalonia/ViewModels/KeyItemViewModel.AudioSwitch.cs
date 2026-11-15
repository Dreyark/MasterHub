using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>An entry of the device lists in the inspector of an "audio device switch" key.</summary>
public sealed record AudioDeviceOption(string Id, string Name)
{
    public override string ToString() => AudioDeviceNames.Normalize(Name);
}

/// <summary>"Switch audio device" key (T-16): one press toggles between two devices (speakers ⇄ headphones, or two microphones).</summary>
public partial class KeyItemViewModel
{
    public bool IsAudioSwitchAction => AssignedAction == DeckActionType.AudioOutputSwitch;

    private AudioSwitchParameter AudioSwitch => AudioSwitchParameter.Parse(ActionParameter);

    /// <summary>False = playback devices, true = microphones. Changing it forgets the two devices (they are of the other kind).</summary>
    public bool AudioSwitchCapture
    {
        get => AudioSwitch.Capture;
        set
        {
            if (AudioSwitch.Capture == value) return;
            ChangeAudioSwitch(_ => AudioSwitchParameter.Empty(value));
        }
    }

    /// <summary>The devices that exist now, of the selected kind.</summary>
    public IReadOnlyList<AudioDeviceOption> AudioSwitchDevices =>
        (GetService<IAudioDeviceService>()?.GetDevices(AudioSwitchCapture) ?? new List<AudioDeviceInfo>())
        .Select(d => new AudioDeviceOption(d.Id, d.Name)).ToList();

    public AudioDeviceOption? AudioSwitchDeviceA
    {
        get => OptionFor(AudioSwitch.A);
        set => SetDevice(first: true, value);
    }

    public AudioDeviceOption? AudioSwitchDeviceB
    {
        get => OptionFor(AudioSwitch.B);
        set => SetDevice(first: false, value);
    }

    /// <summary>Localised warning when a saved device is missing (unplugged), so the problem is seen before pressing the key.</summary>
    public string AudioSwitchProblemText
    {
        get
        {
            var parameter = AudioSwitch;
            if (!parameter.IsComplete) return "";
            var devices = GetService<IAudioDeviceService>()?.GetAllDevices();
            if (devices == null) return "";

            var loc = LocalizationService.Instance;
            return AudioSwitchPlanner.Plan(devices, parameter).Problem switch
            {
                AudioSwitchProblem.FirstMissing => loc.GetString("AudioSwitch_FirstMissing", "The first device is not connected."),
                AudioSwitchProblem.SecondMissing => loc.GetString("AudioSwitch_SecondMissing", "The second device is not connected."),
                AudioSwitchProblem.BothMissing => loc.GetString("AudioSwitch_BothMissing", "Neither device is connected."),
                AudioSwitchProblem.SameDevice => loc.GetString("AudioSwitch_SameDevice", "Both slots hold the same device."),
                _ => ""
            };
        }
    }

    public bool HasAudioSwitchProblem => AudioSwitchProblemText.Length > 0;

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private void SetAudioSwitchKind(string kind) => AudioSwitchCapture = kind == "input";

    private AudioDeviceOption? OptionFor(SavedAudioDevice saved)
    {
        if (string.IsNullOrEmpty(saved.Id) && string.IsNullOrEmpty(saved.Name)) return null;
        var devices = AudioSwitchDevices;
        var info = AudioDeviceMatcher.Resolve(
            devices.Select(d => new AudioDeviceInfo(d.Id, d.Name, AudioSwitchCapture, false)).ToList(), saved);
        // A saved device that is not connected stays visible (by its saved name) instead of silently disappearing.
        return info == null ? new AudioDeviceOption(saved.Id, saved.Name) : devices.First(d => d.Id == info.Id);
    }

    private void SetDevice(bool first, AudioDeviceOption? option)
    {
        if (option == null) return;
        var saved = new SavedAudioDevice(option.Id, option.Name);
        ChangeAudioSwitch(cur => first ? cur with { A = saved } : cur with { B = saved });
    }

    /// <summary>
    /// Changes the parameter and keeps the key's texts in step: the title and subtitle are only replaced while they still are
    /// the automatic ones (a label the user typed is never overwritten).
    /// </summary>
    private void ChangeAudioSwitch(System.Func<AudioSwitchParameter, AudioSwitchParameter> change)
    {
        string oldTitle = AudioSwitchTitle();
        string oldSubtitle = AudioSwitchSubtitle();

        ActionParameter = change(AudioSwitch).Format();

        if (CustomTitle == oldTitle) CustomTitle = AudioSwitchTitle();
        if (CustomSubtitle == oldSubtitle) CustomSubtitle = AudioSwitchSubtitle();
    }

    /// <summary>Title of the key: what kind of device it switches.</summary>
    private string AudioSwitchTitle() => LocalizationService.Instance.GetString(
        AudioSwitch.Capture ? "Deck_AudioInput" : "Deck_AudioOutput", AudioSwitch.Capture ? "INPUT" : "OUTPUT");

    /// <summary>Subtitle of the key: both device names, the active one first (shown from the toggle state).</summary>
    private string AudioSwitchSubtitle()
    {
        var p = AudioSwitch;
        if (!p.IsComplete) return LocalizationService.Instance.GetString("Sub_AudioSwitchSetup", "Pick two devices");

        static string Short(string name) { string n = AudioDeviceNames.Normalize(name); return n.Length > 11 ? n[..10] + "…" : n; }
        return IsActiveToggleState ? $"{Short(p.A.Name)} ⇄ {Short(p.B.Name)}" : $"{Short(p.B.Name)} ⇄ {Short(p.A.Name)}";
    }

    /// <summary>Called when the action or its parameter changed so the inspector section and the key texts refresh.</summary>
    private void NotifyAudioSwitchChanged()
    {
        OnPropertyChanged(nameof(IsAudioSwitchAction));
        OnPropertyChanged(nameof(AudioSwitchCapture));
        OnPropertyChanged(nameof(AudioSwitchDevices));
        OnPropertyChanged(nameof(AudioSwitchDeviceA));
        OnPropertyChanged(nameof(AudioSwitchDeviceB));
        OnPropertyChanged(nameof(AudioSwitchProblemText));
        OnPropertyChanged(nameof(HasAudioSwitchProblem));
        OnPropertyChanged(nameof(DisplaySubtitle));
    }
}
