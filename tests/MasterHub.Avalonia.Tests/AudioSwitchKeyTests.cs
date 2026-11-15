using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Localization;

namespace MasterHub.UiTests;

/// <summary>T-16 in the Studio: the key's parameter, texts and inspector (no real audio devices involved).</summary>
public class AudioSwitchKeyTests
{
    private static KeyItemViewModel NewKey() => new() { Index = 0, AssignedAction = DeckActionType.AudioOutputSwitch };

    [AvaloniaFact]
    public void FreshKey_AsksForTwoDevices()
    {
        var key = NewKey();

        Assert.True(key.IsAudioSwitchAction);
        Assert.False(AudioSwitchParameter.Parse(key.ActionParameter).IsComplete);
        Assert.Equal(LocalizationService.Instance.GetString("Sub_AudioSwitchSetup"), key.CustomSubtitle);
    }

    [AvaloniaFact]
    public void PickingDevices_BuildsTheParameter_AndTheSubtitleShowsBothNames()
    {
        var key = NewKey();

        key.AudioSwitchDeviceA = new AudioDeviceOption("id-a", "2 - Speakers (Realtek)");
        key.AudioSwitchDeviceB = new AudioDeviceOption("id-b", "Headphones (USB)");

        var p = AudioSwitchParameter.Parse(key.ActionParameter);
        Assert.True(p.IsComplete);
        Assert.Equal("id-a", p.A.Id);
        Assert.Equal("Headphones (USB)", p.B.Name);
        Assert.Contains("⇄", key.CustomSubtitle);
        Assert.Contains("Speakers", key.CustomSubtitle);   // the Windows number is not shown
        Assert.DoesNotContain("2 -", key.CustomSubtitle);
    }

    [AvaloniaFact]
    public void ChangingTheKind_ForgetsTheDevices_AndRetitlesTheKey()
    {
        var key = NewKey();
        key.AudioSwitchDeviceA = new AudioDeviceOption("a", "Speakers");
        key.AudioSwitchDeviceB = new AudioDeviceOption("b", "Headphones");
        Assert.Equal(LocalizationService.Instance.GetString("Deck_AudioOutput"), key.CustomTitle);

        key.AudioSwitchCapture = true;

        Assert.True(AudioSwitchParameter.Parse(key.ActionParameter).Capture);
        Assert.False(AudioSwitchParameter.Parse(key.ActionParameter).IsComplete);
        Assert.Equal(LocalizationService.Instance.GetString("Deck_AudioInput"), key.CustomTitle);
    }

    [AvaloniaFact]
    public void ATitleTheUserTyped_IsNeverOverwritten()
    {
        var key = NewKey();
        key.CustomTitle = "My sound";

        key.AudioSwitchDeviceA = new AudioDeviceOption("a", "Speakers");
        key.AudioSwitchCapture = true;

        Assert.Equal("My sound", key.CustomTitle);
    }

    [AvaloniaFact]
    public void Inspector_Section_Renders()
    {
        var key = NewKey();
        key.AudioSwitchDeviceA = new AudioDeviceOption("a", "Speakers (Realtek)");
        Assert.True(File.Exists(Shot.Render(new KeyAudioSwitchSettingsView { DataContext = key }, "inspector_audio_switch", 340, 420)));
    }
}
