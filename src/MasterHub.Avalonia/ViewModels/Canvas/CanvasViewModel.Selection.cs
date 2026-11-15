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

    [RelayCommand]
    public void SelectKey(KeyItemViewModel key)
    {
        ClearSelection();
        key.IsSelected = true;
        SelectedElement = key;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectSlider(SliderItemViewModel slider)
    {
        ClearSelection();
        slider.IsSelected = true;
        SelectedElement = slider;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectRoller(RollerItemViewModel roller)
    {
        ClearSelection();
        roller.IsSelected = true;
        SelectedElement = roller;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectBase()
    {
        ClearSelection();
        BaseStation.IsSelected = true;
        SelectedElement = BaseStation;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectKnob(KnobItemViewModel knob)
    {
        ClearSelection();
        knob.IsSelected = true;
        SelectedElement = knob;
        NotifySelectionChanged();
    }

    [RelayCommand]
    public void SelectDial()
    {
        if (Dial == null) return;
        ClearSelection();
        Dial.IsSelected = true;
        SelectedElement = Dial;
        NotifySelectionChanged();
    }

    private void ClearSelection()
    {
        foreach (var k in Keys)
        {
            k.IsSelected = false;
            k.IsPressed = false;
        }
        foreach (var s in Sliders) s.IsSelected = false;
        foreach (var r in Rollers) r.IsSelected = false;
        foreach (var kn in Knobs) kn.IsSelected = false;
        if (Dial != null) Dial.IsSelected = false;
        BaseStation.IsSelected = false;
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(IsKeySelected));
        OnPropertyChanged(nameof(IsSliderSelected));
        OnPropertyChanged(nameof(IsRollerSelected));
        OnPropertyChanged(nameof(IsKnobSelected));
        OnPropertyChanged(nameof(IsDialSelected));
        OnPropertyChanged(nameof(IsBaseSelected));
        OnPropertyChanged(nameof(SelectedKey));
        OnPropertyChanged(nameof(SelectedSlider));
        OnPropertyChanged(nameof(SelectedRoller));
        OnPropertyChanged(nameof(SelectedKnob));
        OnPropertyChanged(nameof(SelectedDial));
        OnPropertyChanged(nameof(SelectedBase));
    }
}
