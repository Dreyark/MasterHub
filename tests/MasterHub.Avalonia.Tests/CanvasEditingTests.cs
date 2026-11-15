using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Actions;
using MasterHub.Core.Audio;
using MasterHub.Core.Integrations.Discord;
using MasterHub.Core.Integrations.JetKvm;
using MasterHub.Core.Integrations.Kick;
using MasterHub.Core.Integrations.Obs;
using MasterHub.Core.Integrations.Spotify;
using MasterHub.Core.Integrations.Streamlabs;
using MasterHub.Core.Integrations.Teams;
using MasterHub.Core.Integrations.Twitch;
using MasterHub.Core.Integrations.Vlc;
using MasterHub.Core.Integrations.Voicemod;
using MasterHub.Core.Integrations.YouTube;
using MasterHub.Core.Media;
using MasterHub.Core.Monitoring;
using MasterHub.Core.Profiles;
using MasterHub.Core.Razer;
using MasterHub.Core.Rendering;
using MasterHub.Core.Security;
using MasterHub.Core.SystemControl;
using MasterHub.Core.Wooting;
using MasterHub.Avalonia.Services;

namespace MasterHub.UiTests;

/// <summary>T-06 on the real Studio view model (stub services): copy/paste, move/swap, paste look, undo/redo.</summary>
public sealed class CanvasEditingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mh_edit_ui_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static T F<T>() where T : class => EmptyProxy<T>.Create();

    private (CanvasViewModel Canvas, ProfileService Profiles) Create()
    {
        var profiles = new ProfileService(_dir, new SecretProtectionService());
        var canvas = new CanvasViewModel(F<IMasterHubDeviceService>(), F<IKeyRenderer>(), F<IAnimatedKeyRenderer>(), F<ISystemControlService>(),
            F<IAudioService>(), F<IRazerService>(), F<IWootingService>(), F<ISystemMonitorService>(), F<INowPlayingService>(),
            F<IObsService>(), F<IDiscordService>(), F<ITeamsService>(), F<ITwitchService>(), F<IKickService>(), F<IJetKvmService>(),
            F<ISpotifyService>(), F<IVlcService>(), F<IVoicemodService>(), F<IYouTubeService>(), F<IStreamlabsService>(), profiles);
        canvas.ApplyProfile(profiles.GetActiveProfile());
        return (canvas, profiles);
    }

    private static void Give(CanvasViewModel canvas, int index, DeckActionType action, string title, string background = "#000000")
    {
        canvas.Keys[index].AssignedAction = action;
        canvas.Keys[index].CustomTitle = title;
        canvas.Keys[index].BackgroundColorHex = background;
    }

    [AvaloniaFact]
    public void CopyPaste_DuplicatesAKey_AndTheOriginalStays()
    {
        var (canvas, profiles) = Create();
        Give(canvas, 1, DeckActionType.MediaPlayPause, "Play");

        canvas.CopyKey(canvas.Keys[1]);
        Assert.True(canvas.HasKeyClipboard);
        canvas.PasteKey(canvas.Keys[6]);

        Assert.Equal(DeckActionType.MediaPlayPause, canvas.Keys[6].AssignedAction);
        Assert.Equal("Play", canvas.Keys[6].CustomTitle);
        Assert.Equal(DeckActionType.MediaPlayPause, canvas.Keys[1].AssignedAction);
        Assert.Equal(DeckActionType.MediaPlayPause, profiles.GetActiveProfile().Keys.Single(k => k.Index == 6).Action); // saved
    }

    [AvaloniaFact]
    public void Move_OntoEmpty_Moves_OntoOccupied_Swaps()
    {
        var (canvas, _) = Create();
        Give(canvas, 1, DeckActionType.MediaPlayPause, "Play");
        Give(canvas, 2, DeckActionType.MediaNext, "Next");

        canvas.MoveKey(1, 8);
        Assert.Equal(DeckActionType.None, canvas.Keys[1].AssignedAction);
        Assert.Equal(DeckActionType.MediaPlayPause, canvas.Keys[8].AssignedAction);

        canvas.MoveKey(8, 2);
        Assert.Equal(DeckActionType.MediaNext, canvas.Keys[8].AssignedAction);
        Assert.Equal(DeckActionType.MediaPlayPause, canvas.Keys[2].AssignedAction);
    }

    [AvaloniaFact]
    public void Cut_RemovesTheKey_AndPasteBringsItBack()
    {
        var (canvas, _) = Create();
        Give(canvas, 3, DeckActionType.MediaStop, "Stop");

        canvas.CutKey(canvas.Keys[3]);
        Assert.Equal(DeckActionType.None, canvas.Keys[3].AssignedAction);

        canvas.PasteKey(canvas.Keys[10]);
        Assert.Equal(DeckActionType.MediaStop, canvas.Keys[10].AssignedAction);
    }

    [AvaloniaFact]
    public void PasteLook_KeepsTheTargetsAction()
    {
        var (canvas, _) = Create();
        Give(canvas, 1, DeckActionType.MediaPlayPause, "Play", background: "#AA0000");
        Give(canvas, 2, DeckActionType.MediaNext, "Next", background: "#0000AA");

        canvas.CopyKey(canvas.Keys[1]);
        canvas.PasteLook(canvas.Keys[2]);

        Assert.Equal("#AA0000", canvas.Keys[2].BackgroundColorHex);
        Assert.Equal(DeckActionType.MediaNext, canvas.Keys[2].AssignedAction);
        Assert.Equal("Next", canvas.Keys[2].CustomTitle);
    }

    [AvaloniaFact]
    public void Undo_Redo_StepThroughEdits()
    {
        var (canvas, _) = Create();
        Give(canvas, 1, DeckActionType.MediaPlayPause, "Play");

        canvas.CopyKey(canvas.Keys[1]);
        canvas.PasteKey(canvas.Keys[4]);
        canvas.PasteKey(canvas.Keys[5]);
        Assert.True(canvas.CanUndoEdit);

        canvas.UndoEdit();
        Assert.Equal(DeckActionType.None, canvas.Keys[5].AssignedAction);
        Assert.Equal(DeckActionType.MediaPlayPause, canvas.Keys[4].AssignedAction);

        canvas.UndoEdit();
        Assert.Equal(DeckActionType.None, canvas.Keys[4].AssignedAction);
        Assert.True(canvas.CanRedoEdit);

        canvas.RedoEdit();
        canvas.RedoEdit();
        Assert.Equal(DeckActionType.MediaPlayPause, canvas.Keys[5].AssignedAction);
        Assert.False(canvas.CanRedoEdit);
    }

    [AvaloniaFact]
    public void RapidPastes_AreAllApplied()
    {
        var (canvas, _) = Create();
        Give(canvas, 0, DeckActionType.MediaNext, "N");
        canvas.CopyKey(canvas.Keys[0]);

        for (int i = 1; i <= 8; i++) canvas.PasteKey(canvas.Keys[i]);

        Assert.All(Enumerable.Range(1, 8), i => Assert.Equal(DeckActionType.MediaNext, canvas.Keys[i].AssignedAction));
        for (int i = 0; i < 8; i++) canvas.UndoEdit();
        Assert.All(Enumerable.Range(1, 8), i => Assert.Equal(DeckActionType.None, canvas.Keys[i].AssignedAction));
    }

    [AvaloniaFact]
    public void NoOp_EditsLeaveNoHistoryEntry()
    {
        var (canvas, _) = Create();

        canvas.ClearKey(canvas.Keys[2]);   // already empty
        canvas.MoveKey(3, 3);              // onto itself

        Assert.False(canvas.CanUndoEdit);
    }

    [AvaloniaFact]
    public void SwitchingProfile_ClearsTheHistory()
    {
        var (canvas, profiles) = Create();
        Give(canvas, 1, DeckActionType.MediaPlayPause, "Play");
        canvas.CopyKey(canvas.Keys[1]);
        canvas.PasteKey(canvas.Keys[2]);
        Assert.True(canvas.CanUndoEdit);

        var other = profiles.CreateProfile("Other", setAsActive: true);
        canvas.ApplyProfile(other);

        Assert.False(canvas.CanUndoEdit);
        canvas.UndoEdit(); // nothing to undo, nothing breaks
        Assert.Equal(DeckActionType.None, canvas.Keys[2].AssignedAction);
    }

    [AvaloniaFact]
    public void ClearKey_ThenUndo_RestoresIt()
    {
        var (canvas, _) = Create();
        Give(canvas, 7, DeckActionType.MediaStop, "Stop");

        canvas.ClearKey(canvas.Keys[7]);
        Assert.Equal(DeckActionType.None, canvas.Keys[7].AssignedAction);

        canvas.UndoEdit();
        Assert.Equal(DeckActionType.MediaStop, canvas.Keys[7].AssignedAction);
        Assert.Equal("Stop", canvas.Keys[7].CustomTitle);
    }
}

public class KeyContextMenuTests
{
    [AvaloniaFact]
    public void KeyView_BuildsItsContextMenu_WithTheEditingCommands()
    {
        var view = new MasterHub.Avalonia.Views.Canvas.KeyItemView();
        var border = Assert.IsType<global::Avalonia.Controls.Border>(view.Content);
        var menu = Assert.IsType<global::Avalonia.Controls.ContextMenu>(border.ContextMenu);

        var names = menu.Items.OfType<global::Avalonia.Controls.MenuItem>().Select(i => i.Name).ToArray();
        Assert.Equal(new[] { "MenuCopy", "MenuCut", "MenuPaste", "MenuPasteLook", "MenuClear", "MenuClearAll", "MenuUndo", "MenuRedo" }, names);
        Assert.Contains("Ctrl+Shift+V", menu.Items.OfType<global::Avalonia.Controls.MenuItem>().Select(i => i.InputGesture?.ToString()));
    }
}
