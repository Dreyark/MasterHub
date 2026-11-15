using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Canvas;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;

namespace MasterHub.UiTests;

/// <summary>T-05 in the Studio: the gesture tabs, the badge and the save/load round trip of a key's extra bindings.</summary>
public class KeyGestureUiTests
{
    private static KeyItemViewModel KeyWithGestures()
    {
        var key = new KeyItemViewModel { Index = 4, AssignedAction = DeckActionType.MediaPlayPause, CustomTitle = "Play" };
        key.HoldAction = DeckActionType.Hotkey;
        key.HoldParameter = "Ctrl+Shift+M";
        key.DoubleAction = DeckActionType.MediaNext;
        return key;
    }

    [AvaloniaFact]
    public void KeyWithoutGestures_HasNoBadge_AndSavesNoGestures()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.MediaNext };

        Assert.False(key.HasGestures);
        Assert.Equal("", key.GestureBadge);
        Assert.Null(key.ToGesturesModel());
        Assert.False(key.GestureBindings.Any);
    }

    [AvaloniaFact]
    public void Gestures_RoundTripThroughTheModel_AndShowABadge()
    {
        var key = KeyWithGestures();

        Assert.True(key.HasGestures);
        Assert.Equal("H·D", key.GestureBadge);
        Assert.Equal(new GestureBindings(true, true), key.GestureBindings);

        var model = key.ToGesturesModel()!;
        Assert.Equal("Ctrl+Shift+M", model.Hold!.ActionParameter);

        var loaded = new KeyItemViewModel { Index = 4 };
        loaded.LoadGestures(model);
        Assert.Equal(DeckActionType.Hotkey, loaded.HoldAction);
        Assert.Equal(DeckActionType.MediaNext, loaded.DoubleAction);
        Assert.Equal("H·D", loaded.GestureBadge);

        loaded.LoadGestures(null);
        Assert.False(loaded.HasGestures);
    }

    [AvaloniaTheory]
    [InlineData(DeckActionType.Hotkey, DeckActionType.None, "H")]
    [InlineData(DeckActionType.None, DeckActionType.MediaNext, "D")]
    public void Badge_ShowsWhichGesturesExist(DeckActionType hold, DeckActionType dbl, string badge)
    {
        var key = new KeyItemViewModel { Index = 1, HoldAction = hold, DoubleAction = dbl };
        Assert.Equal(badge, key.GestureBadge);
    }

    [AvaloniaFact]
    public void DestructiveHold_AsksForTheLongHold_UnlessTheKeyOptedOut()
    {
        var key = new KeyItemViewModel { Index = 2, HoldAction = DeckActionType.SystemShutdown };
        Assert.Equal(PowerActionGuard.HoldToConfirmMs, key.GestureBindings.HoldMs);

        key.SkipPowerConfirmation = true;
        Assert.Null(key.GestureBindings.HoldMs);
    }

    [AvaloniaFact]
    public void ChangingAGesture_NotifiesTheStudioSoItIsSaved()
    {
        var key = new KeyItemViewModel { Index = 0 };
        int saves = 0;
        key.ActionChangedCallback = _ => saves++;

        key.HoldAction = DeckActionType.MediaNext;
        key.HoldParameter = "x";
        key.SelectedDoubleOption = KeyItemViewModel.GestureActionOptions.First(o => o.Action == DeckActionType.MediaStop);

        Assert.Equal(3, saves);
        Assert.Equal(DeckActionType.MediaStop, key.DoubleAction);
    }

    [AvaloniaFact]
    public void TabSelection_SwitchesTheEditor()
    {
        var key = new KeyItemViewModel { Index = 0 };
        Assert.True(key.IsPressTab);

        key.SelectGestureTabCommand.Execute("1");
        Assert.True(key.IsHoldTab);
        Assert.False(key.IsPressTab);

        key.SelectGestureTabCommand.Execute("2");
        Assert.True(key.IsDoubleTab);
    }

    [AvaloniaFact]
    public void GestureOptions_ContainNoneFirst_AndNoPluginActions()
    {
        var options = KeyItemViewModel.GestureActionOptions;

        Assert.Equal(DeckActionType.None, options[0].Action);
        Assert.DoesNotContain(options, o => o.Action == DeckActionType.StreamDeckPluginAction);
        Assert.Contains(options, o => o.Action == DeckActionType.Hotkey);
        Assert.True(options.Count > 30);
    }

    [AvaloniaFact]
    public void Inspector_AndKey_RenderWithGestures()
    {
        var key = KeyWithGestures();
        Assert.True(File.Exists(Shot.Render(new KeyInspectorView { DataContext = key }, "inspector_key_gestures_press", 340, 900)));

        key.SelectGestureTabCommand.Execute("1");
        Assert.True(File.Exists(Shot.Render(new KeyInspectorView { DataContext = key }, "inspector_key_gestures_hold", 340, 900)));

        Assert.True(File.Exists(Shot.Render(new KeyItemView { DataContext = key }, "key_with_gesture_badge", 140, 140)));
    }
}
