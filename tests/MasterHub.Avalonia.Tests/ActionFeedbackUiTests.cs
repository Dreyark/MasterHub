using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Actions;

namespace MasterHub.UiTests;

/// <summary>T-07 in the Studio: the failure flag of a key and its reason.</summary>
public class ActionFeedbackUiTests
{
    [AvaloniaFact]
    public void FailedResult_MarksTheKey_AndASuccessClearsIt()
    {
        var key = new KeyItemViewModel { Index = 3, AssignedAction = DeckActionType.ObsToggleStream };

        Assert.True(key.ApplyActionResult(ActionResult.Failed("OBS is offline")));
        Assert.True(key.LastActionFailed);
        Assert.Equal("OBS is offline", key.LastActionError);

        Assert.False(key.ApplyActionResult(ActionResult.Failed("OBS is offline")));   // same failure: no redraw needed
        Assert.False(key.ApplyActionResult(ActionResult.NotApplicable));
        Assert.True(key.LastActionFailed);

        Assert.True(key.ApplyActionResult(ActionResult.Ok));
        Assert.False(key.LastActionFailed);
        Assert.Equal("", key.LastActionError);
    }

    [AvaloniaFact]
    public void ClearActionResult_ForgetsAFailure_WhenAProfileIsLoaded()
    {
        var key = new KeyItemViewModel { Index = 0 };
        key.ApplyActionResult(ActionResult.Failed("x"));

        key.ClearActionResult();

        Assert.False(key.LastActionFailed);
    }

    [AvaloniaFact]
    public void Inspector_ShowsTheReason()
    {
        var key = new KeyItemViewModel { Index = 0, AssignedAction = DeckActionType.TwitchCreateClip };
        key.ApplyActionResult(ActionResult.Failed("Twitch rejected the request (no clips:edit scope)"));

        Assert.True(File.Exists(Shot.Render(new KeyActionResultView { DataContext = key }, "inspector_action_failed", 340, 140)));
    }
}
