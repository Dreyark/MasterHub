using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Actions;
using MasterHub.Core.LiveValues;

namespace MasterHub.UiTests;

/// <summary>T-11 in the Studio: the editor, persistence of the settings and the severity colour of the key.</summary>
public class LiveValueKeyTests
{
    private static KeyItemViewModel NewKey() => new() { Index = 0, AssignedAction = DeckActionType.LiveValue };

    [AvaloniaFact]
    public void EditorChanges_UpdateTheSettings_AndNotifyTheStudio()
    {
        var key = NewKey();
        int saves = 0;
        key.ActionChangedCallback = _ => saves++;

        var editor = key.LiveEditor;
        editor.SelectedSource = editor.Sources.First(s => s.Kind == LiveValueSourceKind.WebAddress);
        editor.Url = "https://api.test/price";
        editor.JsonPath = "data.items[0].price";
        editor.RefreshSeconds = 1;                 // clamped to the 5 s floor
        editor.WarnAtText = "70";
        editor.AlertAtText = "90";
        editor.WorseWhenHigher = false;

        var s = key.LiveSettings;
        Assert.Equal(LiveValueSourceKind.WebAddress, s.Kind);
        Assert.Equal("https://api.test/price", s.Url);
        Assert.Equal(5, s.RefreshSeconds);
        Assert.Equal(70, s.WarnAt);
        Assert.Equal(90, s.AlertAt);
        Assert.Equal(WorseWhen.Lower, s.Worse);
        Assert.True(saves >= 6);
        Assert.True(editor.ShowsUrl);
        Assert.True(editor.ShowsPath);
        Assert.False(editor.ShowsCity);
    }

    [AvaloniaFact]
    public void Settings_AreSavedOnlyForLiveValueKeys_AndRoundTrip()
    {
        var key = NewKey();
        key.LiveEditor.SelectedSource = key.LiveEditor.Sources.First(s => s.Kind == LiveValueSourceKind.Weather);
        key.LiveEditor.City = "Warsaw";

        var model = key.ToLiveModel()!;
        var loaded = new KeyItemViewModel { Index = 1, AssignedAction = DeckActionType.LiveValue };
        loaded.LoadLiveValue(model);

        Assert.Equal("Warsaw", loaded.LiveSettings.City);
        Assert.Equal(LiveValueSourceKind.Weather, loaded.LiveEditor.SelectedSource.Kind);

        var other = new KeyItemViewModel { Index = 2, AssignedAction = DeckActionType.MediaNext };
        Assert.Null(other.ToLiveModel());
    }

    [AvaloniaFact]
    public void TheProfile_NeverCarriesATokenValue_OnlyItsName()
    {
        var s = new LiveValueSettings { Kind = LiveValueSourceKind.WebAddress, Url = "https://x", TokenName = "live-abc123" };
        string json = System.Text.Json.JsonSerializer.Serialize(s);

        Assert.Contains("live-abc123", json);
        Assert.DoesNotContain("Bearer", json);
        Assert.DoesNotContain("Token\":\"", json.Replace("TokenName", ""));
    }

    [AvaloniaFact]
    public void Severity_ColoursTheKey_AndNormalKeepsTheChosenBackground()
    {
        var key = NewKey();
        key.BackgroundColorHex = "#112233";

        Assert.Equal("#112233", key.RenderBackgroundHex);

        key.LiveSeverity = LiveSeverity.Warning;
        Assert.NotEqual("#112233", key.RenderBackgroundHex);

        key.LiveSeverity = LiveSeverity.Alert;
        Assert.NotEqual("#112233", key.RenderBackgroundHex);

        key.LiveSeverity = LiveSeverity.Normal;
        Assert.Equal("#112233", key.RenderBackgroundHex);
    }

    [AvaloniaFact]
    public void LiveValueKey_IsAMonitoringKey_SoItsValueIsShownAsTheSubtitle()
    {
        var key = NewKey();
        key.LiveValueText = "42.5 USD";

        Assert.True(key.IsMonitoringAction);
        Assert.Equal("42.5 USD", key.GetEffectiveSubtitle());
    }

    [AvaloniaFact]
    public void Inspector_Section_Renders()
    {
        var key = NewKey();
        key.LiveEditor.SelectedSource = key.LiveEditor.Sources.First(s => s.Kind == LiveValueSourceKind.Command);
        key.LiveEditor.Command = "echo 42";

        Assert.True(File.Exists(Shot.Render(new KeyLiveValueSettingsView { DataContext = key }, "inspector_live_value", 340, 900)));
    }
}
