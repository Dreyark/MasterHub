using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.HomeAssistant;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.StreamDeck;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Models;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{
    private bool TryExecuteRazerAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Razer Hardware
            case DeckActionType.RazerDpiCycle:
                if (int.TryParse(key.ActionParameter, out int dpi) && dpi > 0)
                    _razerService.SetDpi(dpi);
                else
                    _razerService.CycleDpiNext();
                break;
            case DeckActionType.RazerDpiUp:
                _razerService.CycleDpi(1);
                break;
            case DeckActionType.RazerDpiDown:
                _razerService.CycleDpi(-1);
                break;
            case DeckActionType.RazerDpi400:
                _razerService.SetDpi(400);
                break;
            case DeckActionType.RazerDpi800:
                _razerService.SetDpi(800);
                break;
            case DeckActionType.RazerDpi1600:
                _razerService.SetDpi(1600);
                break;
            case DeckActionType.RazerDpi3200:
                _razerService.SetDpi(3200);
                break;
            case DeckActionType.RazerDpi6400:
                _razerService.SetDpi(6400);
                break;
            case DeckActionType.RazerPollingRateCycle:
                if (int.TryParse(key.ActionParameter.Replace("Hz", "").Trim(), out int hz) && hz > 0)
                    _razerService.SetPollingRate(hz);
                else
                    _razerService.CyclePollingRateNext();
                break;
            case DeckActionType.RazerPollingRateUp:
                _razerService.CyclePollingRate(1);
                break;
            case DeckActionType.RazerPollingRateDown:
                _razerService.CyclePollingRate(-1);
                break;
            case DeckActionType.RazerPollingRate125:
                _razerService.SetPollingRate(125);
                break;
            case DeckActionType.RazerPollingRate500:
                _razerService.SetPollingRate(500);
                break;
            case DeckActionType.RazerPollingRate1000:
                _razerService.SetPollingRate(1000);
                break;
            case DeckActionType.RazerPollingRate2000:
                _razerService.SetPollingRate(2000);
                break;
            case DeckActionType.RazerPollingRate4000:
                _razerService.SetPollingRate(4000);
                break;
            case DeckActionType.RazerPollingRate8000:
                _razerService.SetPollingRate(8000);
                break;
            case DeckActionType.RazerProfileNext:
                _razerService.CycleProfile(1);
                break;
            case DeckActionType.RazerProfilePrev:
                _razerService.CycleProfile(-1);
                break;
            case DeckActionType.RazerProfile1:
                if (int.TryParse(key.ActionParameter.Replace("Profil", "").Replace("#", "").Trim(), out int prof) && prof >= 1 && prof <= 5)
                    _razerService.SwitchProfile(prof);
                else
                    _razerService.SwitchProfile(1);
                break;
            case DeckActionType.RazerProfile2:
                _razerService.SwitchProfile(2);
                break;
            case DeckActionType.RazerProfile3:
                _razerService.SwitchProfile(3);
                break;
            case DeckActionType.RazerProfile4:
                _razerService.SwitchProfile(4);
                break;
            case DeckActionType.RazerProfile5:
                _razerService.SwitchProfile(5);
                break;

            default:
                return false;
        }

        return true;
    }

    private bool TryExecuteWootingAction(KeyItemViewModel key)
    {
        switch (key.AssignedAction)
        {
            // Wooting
            case DeckActionType.WootingProfileCycle:
            case DeckActionType.WootingProfileNext:
                _wootingService.CycleProfileNext();
                break;
            case DeckActionType.WootingProfilePrev:
                _wootingService.CycleProfile(-1);
                break;
            case DeckActionType.WootingProfileDigital:
                _wootingService.SwitchProfile(0);
                break;
            case DeckActionType.WootingProfile1:
                _wootingService.SwitchProfile(1);
                break;
            case DeckActionType.WootingProfile2:
                _wootingService.SwitchProfile(2);
                break;
            case DeckActionType.WootingProfile3:
                _wootingService.SwitchProfile(3);
                break;

            default:
                return false;
        }

        return true;
    }
}
