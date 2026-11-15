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

    public bool IsGpuMonitorAction =>
        AssignedAction == DeckActionType.DisplayGpuUsage || AssignedAction == DeckActionType.DisplayGpuTemp;

    public bool IsGpuAction =>
        AssignedAction is DeckActionType.DisplayGpuUsage or DeckActionType.DisplayGpuTemp;

    public bool IsRazerDpiAction =>
        AssignedAction is DeckActionType.RazerDpiCycle or DeckActionType.RazerDpiUp or DeckActionType.RazerDpiDown or
        DeckActionType.RazerDpi400 or DeckActionType.RazerDpi800 or DeckActionType.RazerDpi1600 or DeckActionType.RazerDpi3200 or DeckActionType.RazerDpi6400;

    public bool IsRazerPollingAction =>
        AssignedAction is DeckActionType.RazerPollingRateCycle or DeckActionType.RazerPollingRateUp or DeckActionType.RazerPollingRateDown or
        DeckActionType.RazerPollingRate125 or DeckActionType.RazerPollingRate500 or DeckActionType.RazerPollingRate1000 or
        DeckActionType.RazerPollingRate2000 or DeckActionType.RazerPollingRate4000 or DeckActionType.RazerPollingRate8000;

    public bool IsRazerProfileAction =>
        AssignedAction is DeckActionType.RazerProfile1 or DeckActionType.RazerProfile2 or DeckActionType.RazerProfile3 or
        DeckActionType.RazerProfile4 or DeckActionType.RazerProfile5 or DeckActionType.RazerProfileNext or DeckActionType.RazerProfilePrev;

    public bool IsNetworkMonitorAction => AssignedAction == DeckActionType.DisplayNetworkSpeed;
    public bool IsDiscordJoinAction => AssignedAction == DeckActionType.DiscordJoinVoiceChannel;

    public static readonly IReadOnlyList<string> NetworkUnitOptions = new[]
    {
        "MB/s (Bajtowa: KB/s, MB/s, GB/s)",
        "Mb/s (Bitowa: Kb/s, Mb/s, Gb/s)"
    };

    public string SelectedNetworkUnit
    {
        get => ActionParameter?.Contains("b/s", StringComparison.Ordinal) == true || ActionParameter?.Contains("bit", StringComparison.OrdinalIgnoreCase) == true
            ? NetworkUnitOptions[1]
            : NetworkUnitOptions[0];
        set
        {
            ActionParameter = (value == NetworkUnitOptions[1]) ? "Mb/s" : "MB/s";
            OnPropertyChanged();
        }
    }

    public bool IsDiskAction => AssignedAction == DeckActionType.DisplayDiskUsage;

    public IReadOnlyList<string> AvailableDrives
    {
        get
        {
            var drives = GetService<ISystemMonitorService>()?.AvailableDrives;
            return drives != null && drives.Count > 0 ? drives : new[] { "C:\\" };
        }
    }

    public string SelectedDrive
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : (AvailableDrives.Count > 0 ? AvailableDrives[0] : "C:\\");
        set
        {
            ActionParameter = value ?? "C:\\";
            OnPropertyChanged();
        }
    }

    public bool IsGoToProfileAction => AssignedAction == DeckActionType.GoToProfile;
    public bool IsGoToPageAction => AssignedAction == DeckActionType.GoToPage;
    public bool IsOpenFolderAction => AssignedAction == DeckActionType.OpenFolder;
    public bool IsGoBackAction => AssignedAction == DeckActionType.GoBack;
    public bool IsGoHomeAction => AssignedAction == DeckActionType.GoHome;
    public bool IsNextPageAction => AssignedAction == DeckActionType.NextPage;
    public bool IsPreviousPageAction => AssignedAction == DeckActionType.PreviousPage;
    public bool IsNavigationAction => IsGoBackAction || IsGoHomeAction || IsNextPageAction || IsPreviousPageAction;

    public bool IsVoicemodVoiceAction => AssignedAction == DeckActionType.VoicemodVoiceSelect;
    public bool IsVoicemodSoundAction => AssignedAction == DeckActionType.VoicemodPlaySound;
    public bool IsVoicemodBackgroundToggleAction => AssignedAction == DeckActionType.VoicemodBackgroundToggle;
    public bool IsVoicemodAnyAction => AssignedAction is
        DeckActionType.VoicemodVoiceSelect or
        DeckActionType.VoicemodVoiceToggle or
        DeckActionType.VoicemodMuteVoice or
        DeckActionType.VoicemodHearMyVoiceToggle or
        DeckActionType.VoicemodBackgroundToggle or
        DeckActionType.VoicemodPlaySound or
        DeckActionType.VoicemodStopAllSounds;

    public bool IsVoicemodConnected => GetService<IVoicemodService>()?.IsConnected ?? false;

    public string VoicemodConnectionStatusDisplay => IsVoicemodConnected
        ? LocalizationService.Instance.GetString("Voicemod_ConnectedStatus", "🟢 Połączono z Voicemod")
        : LocalizationService.Instance.GetString("Voicemod_DisconnectedStatus", "🔴 Rozłączono z Voicemod");

    public IReadOnlyList<VoicemodVoiceItem> AvailableVoicemodVoices
    {
        get
        {
            var svc = GetService<IVoicemodService>();
            var list = svc?.AvailableVoices;
            if (list != null && list.Count > 0) return list;
            return VoicemodDefaults.StandardVoices;
        }
    }

    public VoicemodVoiceItem? SelectedVoicemodVoice
    {
        get
        {
            string target = ActionParameter;
            if (string.IsNullOrWhiteSpace(target)) return AvailableVoicemodVoices.FirstOrDefault();
            return AvailableVoicemodVoices.FirstOrDefault(v =>
                string.Equals(v.Id, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v.Name, target, StringComparison.OrdinalIgnoreCase))
                ?? AvailableVoicemodVoices.FirstOrDefault();
        }
        set
        {
            if (value != null)
            {
                _actionParameter = value.Id;
                OnPropertyChanged(nameof(ActionParameter));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    public IReadOnlyList<VoicemodSoundItem> AvailableVoicemodSounds
    {
        get
        {
            var svc = GetService<IVoicemodService>();
            var list = svc?.AvailableSounds;
            if (list != null && list.Count > 0) return list;
            return VoicemodDefaults.StandardSounds;
        }
    }

    public VoicemodSoundItem? SelectedVoicemodSound
    {
        get
        {
            string target = ActionParameter;
            if (string.IsNullOrWhiteSpace(target)) return AvailableVoicemodSounds.FirstOrDefault();
            return AvailableVoicemodSounds.FirstOrDefault(s =>
                string.Equals(s.FileName, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(s.Name, target, StringComparison.OrdinalIgnoreCase))
                ?? AvailableVoicemodSounds.FirstOrDefault();
        }
        set
        {
            if (value != null)
            {
                _actionParameter = value.FileName;
                OnPropertyChanged(nameof(ActionParameter));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    [RelayCommand]
    public void OpenVoicemodSettings()
    {
        var nav = GetService<MasterHub.Avalonia.Services.INavigationService>();
        nav?.NavigateTo(MasterHub.Avalonia.Services.NavigationPage.Settings);
        MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel.RequestScrollToVoicemod();
    }

    public bool IsVoicemodBackgroundEffectsEnabled => GetService<IVoicemodService>()?.IsBackgroundEffectsEnabled ?? true;

    public string VoicemodBackgroundEffectsStatusDisplay => IsVoicemodBackgroundEffectsEnabled
        ? LocalizationService.Instance.GetString("Voicemod_BgEffects_On", "🎧 Dźwięk tła (Background FX): WŁĄCZONY")
        : LocalizationService.Instance.GetString("Voicemod_BgEffects_Off", "🔇 Dźwięk tła (Background FX): WYŁĄCZONY (Sam głos)");

    [RelayCommand]
    public async Task ToggleVoicemodBackgroundEffectsAsync()
    {
        var svc = GetService<IVoicemodService>();
        if (svc != null)
        {
            await svc.ToggleBackgroundEffectsAsync();
            OnPropertyChanged(nameof(IsVoicemodBackgroundEffectsEnabled));
            OnPropertyChanged(nameof(VoicemodBackgroundEffectsStatusDisplay));
        }
    }

    public void RefreshVoicemodProperties()
    {
        OnPropertyChanged(nameof(IsVoicemodConnected));
        OnPropertyChanged(nameof(VoicemodConnectionStatusDisplay));
        OnPropertyChanged(nameof(AvailableVoicemodVoices));
        OnPropertyChanged(nameof(SelectedVoicemodVoice));
        OnPropertyChanged(nameof(AvailableVoicemodSounds));
        OnPropertyChanged(nameof(SelectedVoicemodSound));
        OnPropertyChanged(nameof(IsVoicemodBackgroundEffectsEnabled));
        OnPropertyChanged(nameof(VoicemodBackgroundEffectsStatusDisplay));
    }

    public bool IsHomeAssistantAction => AssignedAction is
        DeckActionType.HomeAssistantToggle or
        DeckActionType.HomeAssistantTurnOn or
        DeckActionType.HomeAssistantTurnOff or
        DeckActionType.HomeAssistantCallService;

    public bool IsHomeAssistantConnected => GetService<IHomeAssistantService>()?.IsConnected ?? false;

    public string HomeAssistantConnectionStatusDisplay => IsHomeAssistantConnected
        ? LocalizationService.Instance.GetString("HA_ConnectedStatus", "🟢 Połączono z Home Assistant")
        : LocalizationService.Instance.GetString("HA_DisconnectedStatus", "🔴 Rozłączono z Home Assistant");

    public IReadOnlyList<HomeAssistantEntityModel> AvailableHomeAssistantEntities
    {
        get
        {
            var svc = GetService<IHomeAssistantService>();
            var list = svc?.Entities;
            if (list != null && list.Count > 0) return list;
            return Array.Empty<HomeAssistantEntityModel>();
        }
    }

    public HomeAssistantEntityModel? SelectedHomeAssistantEntity
    {
        get
        {
            string target = ActionParameter;
            if (string.IsNullOrWhiteSpace(target)) return AvailableHomeAssistantEntities.FirstOrDefault();
            return AvailableHomeAssistantEntities.FirstOrDefault(e =>
                string.Equals(e.EntityId, target, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(e.FriendlyName, target, StringComparison.OrdinalIgnoreCase))
                ?? AvailableHomeAssistantEntities.FirstOrDefault();
        }
        set
        {
            if (value != null)
            {
                _actionParameter = value.EntityId;
                OnPropertyChanged(nameof(ActionParameter));
                OnPropertyChanged();
                OnPropertyChanged(nameof(ActionDisplayName));
                OnPropertyChanged(nameof(DisplayTitle));
                OnPropertyChanged(nameof(DisplaySubtitle));
                ActionChangedCallback?.Invoke(this);
            }
        }
    }

    [RelayCommand]
    public void OpenHomeAssistantSettings()
    {
        var nav = GetService<MasterHub.Avalonia.Services.INavigationService>();
        nav?.NavigateTo(MasterHub.Avalonia.Services.NavigationPage.Settings);
        MasterHub.Avalonia.ViewModels.Pages.SettingsPageViewModel.RequestScrollToHomeAssistant();
    }

    public bool IsGenericParameterAction =>
        RequiresActionParameter && !IsGpuAction && !IsRazerDpiAction && !IsRazerPollingAction && !IsRazerProfileAction &&
        !IsFileBrowseSupported && !IsNetworkMonitorAction && !IsDiskAction &&
        !IsGoToProfileAction && !IsGoToPageAction && !IsOpenFolderAction &&
        !IsGoBackAction && !IsGoHomeAction && !IsNextPageAction && !IsPreviousPageAction &&
        !IsVoicemodVoiceAction && !IsVoicemodSoundAction && !IsHomeAssistantAction && !IsStreamDeckAction;

    public bool IsStreamDeckAction => AssignedAction == DeckActionType.StreamDeckPluginAction;
    public bool HasStreamDeckPlugin => IsStreamDeckAction && !string.IsNullOrEmpty(StreamDeckPluginUuid);

    public string StreamDeckPluginName
    {
        get
        {
            var host = GetService<IStreamDeckPluginHost>();
            var pl = host?.GetPlugin(StreamDeckPluginUuid);
            return pl?.Name ?? StreamDeckPluginUuid;
        }
    }

    public string StreamDeckActionName
    {
        get
        {
            var host = GetService<IStreamDeckPluginHost>();
            var act = host?.GetActionManifest(StreamDeckPluginUuid, StreamDeckActionUuid);
            return act?.Name ?? StreamDeckActionUuid;
        }
    }

    public string StreamDeckActionTooltip
    {
        get
        {
            var host = GetService<IStreamDeckPluginHost>();
            var act = host?.GetActionManifest(StreamDeckPluginUuid, StreamDeckActionUuid);
            return act?.Tooltip ?? "";
        }
    }

    public string StreamDeckActionControllers
    {
        get
        {
            var host = GetService<IStreamDeckPluginHost>();
            var act = host?.GetActionManifest(StreamDeckPluginUuid, StreamDeckActionUuid);
            return act?.ControllersDisplay ?? "Klawisze";
        }
    }

    public bool StreamDeckActionHasDial
    {
        get
        {
            var host = GetService<IStreamDeckPluginHost>();
            var act = host?.GetActionManifest(StreamDeckPluginUuid, StreamDeckActionUuid);
            return act?.SupportsEncoder ?? false;
        }
    }

    public bool HasPropertyInspector
    {
        get
        {
            var host = GetService<IStreamDeckPluginHost>();
            return !string.IsNullOrEmpty(host?.GetPropertyInspectorPath(StreamDeckPluginUuid, StreamDeckActionUuid));
        }
    }

    [RelayCommand]
    public void OpenPropertyInspector()
    {
        var host = GetService<IStreamDeckPluginHost>();
        host?.OpenPropertyInspector(Index, StreamDeckPluginUuid, StreamDeckActionUuid, StreamDeckSettingsJson);
    }

    [RelayCommand]
    public async Task SaveStreamDeckSettingsJsonAsync(string? newJson)
    {
        string toSave = newJson ?? StreamDeckSettingsJson;
        if (string.IsNullOrWhiteSpace(toSave)) toSave = "{}";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(toSave);
            toSave = System.Text.Json.JsonSerializer.Serialize(doc.RootElement, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch { }

        StreamDeckSettingsJson = toSave;
        var host = GetService<IStreamDeckPluginHost>();
        if (host != null && !string.IsNullOrEmpty(StreamDeckPluginUuid))
        {
            await host.UpdateKeySettingsAsync(Index, StreamDeckPluginUuid, StreamDeckActionUuid, toSave);
        }
        ActionChangedCallback?.Invoke(this);
    }

    [RelayCommand]
    public async Task ResetStreamDeckSettingsAsync()
    {
        StreamDeckSettingsJson = "{}";
        var host = GetService<IStreamDeckPluginHost>();
        if (host != null && !string.IsNullOrEmpty(StreamDeckPluginUuid))
        {
            await host.UpdateKeySettingsAsync(Index, StreamDeckPluginUuid, StreamDeckActionUuid, "{}");
        }
        ActionChangedCallback?.Invoke(this);
    }

    public IReadOnlyList<string> AvailableGpuNames
    {
        get
        {
            var gpus = GetService<ISystemMonitorService>()?.AvailableGpuNames;
            return gpus != null && gpus.Count > 0 ? gpus : new[] { "Domyślna karta graficzna GPU" };
        }
    }

    public string SelectedGpuName
    {
        get => !string.IsNullOrWhiteSpace(ActionParameter) ? ActionParameter : (AvailableGpuNames.Count > 0 ? AvailableGpuNames[0] : "");
        set
        {
            ActionParameter = value;
            var sysMon = GetService<ISystemMonitorService>();
            if (sysMon != null)
            {
                for (int i = 0; i < AvailableGpuNames.Count; i++)
                {
                    if (AvailableGpuNames[i] == value)
                    {
                        sysMon.SelectedGpuIndex = i;
                        break;
                    }
                }
            }
            OnPropertyChanged();
        }
    }

    public static readonly IReadOnlyList<string> DpiDropdownOptions = new[]
    {
        "400", "800", "1200", "1600", "2000", "2400", "3200", "4000", "6400", "8000", "12000", "16000", "20000", "26000", "30000"
    };

    public static readonly IReadOnlyList<string> PollingDropdownOptions = new[]
    {
        "125 Hz", "250 Hz", "500 Hz", "1000 Hz", "2000 Hz", "4000 Hz", "8000 Hz"
    };

    public static readonly IReadOnlyList<string> ProfilePresets = new[]
    {
        "Profil #1", "Profil #2", "Profil #3", "Profil #4", "Profil #5"
    };

    public string SelectedDpi
    {
        get => ActionParameter;
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
        }
    }

    public string SelectedPollingRate
    {
        get => ActionParameter;
        set
        {
            ActionParameter = value ?? "";
            OnPropertyChanged();
        }
    }

    [RelayCommand]
    public void SelectProfile(string preset)
    {
        ActionParameter = preset;
    }
}
