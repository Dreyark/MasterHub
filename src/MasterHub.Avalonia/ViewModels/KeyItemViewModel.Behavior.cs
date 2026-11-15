using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Localization;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Rendering;

namespace MasterHub.Avalonia.ViewModels;

public partial class KeyItemViewModel
{

    public record PressModeOption(KeyPressMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    public IReadOnlyList<PressModeOption> AvailablePressModes => new PressModeOption[]
    {
        new(KeyPressMode.OnPress, LocalizationService.Instance.GetString("KeyPress_OnPress", "⬇️ Wciśnięcie (natychmiast)")),
        new(KeyPressMode.OnRelease, LocalizationService.Instance.GetString("KeyPress_OnRelease", "⬆️ Zwolnienie (puszczenie)")),
        new(KeyPressMode.OnHold, LocalizationService.Instance.GetString("KeyPress_OnHold", "⏱️ Przytrzymanie (min. 500 ms)")),
        new(KeyPressMode.OnDoublePress, LocalizationService.Instance.GetString("KeyPress_OnDoublePress", "⚡ Podwójne kliknięcie (300 ms)")),
        new(KeyPressMode.OnHoldRepeat, LocalizationService.Instance.GetString("KeyPress_OnHoldRepeat", "🔁 Powtarzanie przy trzymaniu (100 ms)"))
    };

    public PressModeOption SelectedPressModeOption
    {
        get => System.Linq.Enumerable.FirstOrDefault(AvailablePressModes, x => x.Mode == PressMode) ?? AvailablePressModes[0];
        set
        {
            if (value != null && value.Mode != PressMode)
            {
                PressMode = value.Mode;
            }
        }
    }

    public KeyPressMode PressMode
    {
        get => _pressMode;
        set
        {
            if (SetField(ref _pressMode, value))
            {
                OnPropertyChanged(nameof(PressModeDisplay));
                OnPropertyChanged(nameof(SelectedPressModeOption));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    private bool _skipPowerConfirmation;

    /// <summary>Opt-out of hold-to-confirm for shutdown/restart/sleep (T-03). Default: confirmation required.</summary>
    public bool SkipPowerConfirmation
    {
        get => _skipPowerConfirmation;
        set
        {
            if (SetField(ref _skipPowerConfirmation, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    private MasterHub.Core.Audio.MutePressMode _mutePressMode;

    /// <summary>T-17 f: toggle / push-to-talk / push-to-mute for mute keys.</summary>
    public MasterHub.Core.Audio.MutePressMode MutePressMode
    {
        get => _mutePressMode;
        set
        {
            if (SetField(ref _mutePressMode, value))
            {
                OnPropertyChanged(nameof(MutePressModeIndex));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    /// <summary>For the inspector's combo box (0 toggle, 1 push-to-talk, 2 push-to-mute).</summary>
    public int MutePressModeIndex
    {
        get => (int)_mutePressMode;
        set => MutePressMode = (MasterHub.Core.Audio.MutePressMode)Math.Clamp(value, 0, 2);
    }

    /// <summary>Mute keys that can be push-to-talk / push-to-mute.</summary>
    public bool IsMuteAction => AssignedAction is DeckActionType.MicMute or DeckActionType.DiscordMuteToggle or DeckActionType.TeamsMuteToggle;

    /// <summary>True for keys that shut down, restart or suspend the PC (the inspector shows the opt-out only for them).</summary>
    public bool IsPowerConfirmAction => PowerActionGuard.IsDestructive(AssignedAction);

    public string PressModeDisplay => PressMode switch
    {
        KeyPressMode.OnPress => LocalizationService.Instance.GetString("KeyPress_OnPress", "⬇️ Wciśnięcie (natychmiast)"),
        KeyPressMode.OnRelease => LocalizationService.Instance.GetString("KeyPress_OnRelease", "⬆️ Zwolnienie (puszczenie)"),
        KeyPressMode.OnHold => LocalizationService.Instance.GetString("KeyPress_OnHold", "⏱️ Przytrzymanie (min. 500 ms)"),
        KeyPressMode.OnDoublePress => LocalizationService.Instance.GetString("KeyPress_OnDoublePress", "⚡ Podwójne kliknięcie (300 ms)"),
        KeyPressMode.OnHoldRepeat => LocalizationService.Instance.GetString("KeyPress_OnHoldRepeat", "🔁 Powtarzanie przy trzymaniu (100 ms)"),
        _ => PressMode.ToString()
    };

    private KeyStateMode _stateMode = KeyStateMode.Static;
    public KeyStateMode StateMode
    {
        get => _stateMode;
        set
        {
            if (SetField(ref _stateMode, value))
            {
                OnPropertyChanged(nameof(StateModeDisplay));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StateModeDisplay => StateMode switch
    {
        KeyStateMode.Static => LocalizationService.Instance.GetString("KeyState_Static", "🖼️ Statyczny (Stała ikona)"),
        KeyStateMode.Toggle => LocalizationService.Instance.GetString("KeyState_Toggle", "🔁 Przełącznik (2 stany)"),
        KeyStateMode.LiveFeedback => LocalizationService.Instance.GetString("KeyState_LiveFeedback", "📡 Live Feedback (Stan na żywo)"),
        _ => StateMode.ToString()
    };

    public bool IsAutoExtractedImage => !string.IsNullOrWhiteSpace(_imagePath) &&
        _imagePath.Contains("ExtractedIcons", StringComparison.OrdinalIgnoreCase);

    public void AutoExtractIconIfApplicable()
    {
        if (string.IsNullOrWhiteSpace(_actionParameter)) return;

        if (!string.IsNullOrWhiteSpace(_imagePath) && File.Exists(_imagePath) && !IsAutoExtractedImage)
            return;

        if (AssignedAction is DeckActionType.OpenApplication or
            DeckActionType.OpenFile or
            DeckActionType.OpenWebsite or
            DeckActionType.None)
        {
            try
            {
                var extractor = GetService<IAppIconExtractorService>();
                string? iconPath = extractor?.ExtractAndSaveIcon(_actionParameter, 72);
                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
                {
                    ImagePath = iconPath;
                }
            }
            catch { }
        }
    }

    public string ActionParameter
    {
        get => _actionParameter;
        set
        {
            if (SetField(ref _actionParameter, value))
            {
                if (AssignedAction == DeckActionType.ObsSceneSwitch)
                {
                    _obsSceneName = value;
                }
                AutoExtractIconIfApplicable();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                if (IsAudioSwitchAction) NotifyAudioSwitchChanged();
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string ObsSceneName
    {
        get => _obsSceneName;
        set
        {
            if (SetField(ref _obsSceneName, value))
            {
                if (AssignedAction == DeckActionType.ObsSceneSwitch)
                {
                    _actionParameter = value;
                }
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StreamDeckPluginUuid
    {
        get => _streamDeckPluginUuid;
        set
        {
            if (SetField(ref _streamDeckPluginUuid, value))
            {
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(HasStreamDeckPlugin));
                OnPropertyChanged(nameof(StreamDeckPluginName));
                OnPropertyChanged(nameof(StreamDeckActionName));
                OnPropertyChanged(nameof(StreamDeckActionTooltip));
                OnPropertyChanged(nameof(StreamDeckActionControllers));
                OnPropertyChanged(nameof(StreamDeckActionHasDial));
                OnPropertyChanged(nameof(HasPropertyInspector));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StreamDeckActionUuid
    {
        get => _streamDeckActionUuid;
        set
        {
            if (SetField(ref _streamDeckActionUuid, value))
            {
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(HasStreamDeckPlugin));
                OnPropertyChanged(nameof(StreamDeckPluginName));
                OnPropertyChanged(nameof(StreamDeckActionName));
                OnPropertyChanged(nameof(StreamDeckActionTooltip));
                OnPropertyChanged(nameof(StreamDeckActionControllers));
                OnPropertyChanged(nameof(StreamDeckActionHasDial));
                OnPropertyChanged(nameof(HasPropertyInspector));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public string StreamDeckSettingsJson
    {
        get => _streamDeckSettingsJson;
        set
        {
            if (SetField(ref _streamDeckSettingsJson, value))
            {
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
