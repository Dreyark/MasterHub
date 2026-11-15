using MasterHub.Core.Actions;
using MasterHub.Core.Editing;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Tests;

/// <summary>T-06: move / swap / paste / paste look on the saved form of a view's keys, and undo / redo.</summary>
public class KeyEditingTests
{
    private static List<KeyAssignmentModel> Keys()
    {
        var list = Enumerable.Range(0, 15).Select(i => new KeyAssignmentModel { Index = i }).ToList();
        list[1].Action = DeckActionType.MediaPlayPause;
        list[1].CustomTitle = "Play";
        list[1].BackgroundColorHex = "#112233";
        list[1].Gestures = new KeyGestures { Hold = new KeyGestureBinding { Action = DeckActionType.Hotkey, ActionParameter = "Ctrl+M" } };
        list[2].Action = DeckActionType.MediaNext;
        list[2].CustomTitle = "Next";
        return list;
    }

    [Fact]
    public void Move_OntoEmptyKey_MovesAndEmptiesTheSource()
    {
        var keys = Keys();

        Assert.True(KeyEditing.Move(keys, from: 1, to: 5));

        Assert.Equal(DeckActionType.MediaPlayPause, keys[5].Action);
        Assert.Equal(5, keys[5].Index);
        Assert.Equal("Play", keys[5].CustomTitle);
        Assert.NotNull(keys[5].Gestures); // gestures travel with the key
        Assert.True(KeyEditing.IsEmpty(keys[1]));
        Assert.Equal(1, keys[1].Index);
    }

    [Fact]
    public void Move_OntoOccupiedKey_Swaps()
    {
        var keys = Keys();

        KeyEditing.Move(keys, from: 1, to: 2);

        Assert.Equal(DeckActionType.MediaNext, keys[1].Action);
        Assert.Equal(1, keys[1].Index);
        Assert.Equal(DeckActionType.MediaPlayPause, keys[2].Action);
        Assert.Equal(2, keys[2].Index);
    }

    [Fact]
    public void Move_OntoItself_ChangesNothing()
    {
        var keys = Keys();
        Assert.False(KeyEditing.Move(keys, 1, 1));
        Assert.Equal("Play", keys[1].CustomTitle);
    }

    [Fact]
    public void Swap_ExchangesTwoKeys_KeepingTheirIndexes()
    {
        var keys = Keys();
        KeyEditing.Swap(keys, 1, 2);
        Assert.Equal("Next", keys[1].CustomTitle);
        Assert.Equal("Play", keys[2].CustomTitle);
        Assert.Equal(new[] { 1, 2 }, new[] { keys[1].Index, keys[2].Index });
    }

    [Fact]
    public void Paste_CopiesEverything_AndTheCopyIsIndependent()
    {
        var keys = Keys();
        var clipboard = KeyEditing.Clone(keys[1], 1);

        KeyEditing.Paste(keys, 7, clipboard);
        keys[7].CustomTitle = "changed";
        keys[7].Gestures!.Hold!.ActionParameter = "x";

        Assert.Equal(DeckActionType.MediaPlayPause, keys[7].Action);
        Assert.Equal(7, keys[7].Index);
        Assert.Equal("Play", clipboard.CustomTitle);
        Assert.Equal("Ctrl+M", clipboard.Gestures!.Hold!.ActionParameter);
    }

    [Fact]
    public void Clear_EmptiesTheKey()
    {
        var keys = Keys();
        KeyEditing.Clear(keys, 1);
        Assert.True(KeyEditing.IsEmpty(keys[1]));
        Assert.Equal(1, keys[1].Index);
    }

    [Fact]
    public void PasteLook_CopiesAppearance_ButNeverTheAction()
    {
        var from = new KeyAssignmentModel
        {
            Index = 1, Action = DeckActionType.MediaPlayPause, ActionParameter = "secret", CustomTitle = "Source",
            PressMode = KeyPressMode.OnRelease,
            BackgroundColorHex = "#AA0000", TextColorHex = "#00FF00", ImagePath = @"C:\img.png", ImageScale = 140,
            FontFamily = "Consolas", TitleAlignment = "Left",
            Gestures = new KeyGestures { Hold = new KeyGestureBinding { Action = DeckActionType.MediaNext } }
        };
        var to = new KeyAssignmentModel
        {
            Index = 9, Action = DeckActionType.Hotkey, ActionParameter = "Ctrl+Z", CustomTitle = "Undo",
            PressMode = KeyPressMode.OnPress
        };

        KeyEditing.PasteLook(from, to);

        Assert.Equal("#AA0000", to.BackgroundColorHex);
        Assert.Equal("#00FF00", to.TextColorHex);
        Assert.Equal(@"C:\img.png", to.ImagePath);
        Assert.Equal(140, to.ImageScale);
        Assert.Equal("Consolas", to.FontFamily);
        Assert.Equal("Left", to.TitleAlignment);

        Assert.Equal(DeckActionType.Hotkey, to.Action);
        Assert.Equal("Ctrl+Z", to.ActionParameter);
        Assert.Equal("Undo", to.CustomTitle);
        Assert.Equal(KeyPressMode.OnPress, to.PressMode);
        Assert.Null(to.Gestures);
        Assert.Equal(9, to.Index);
    }

    [Fact]
    public void IsEmpty_ConsidersActionGesturesLabelsAndPictures()
    {
        Assert.True(KeyEditing.IsEmpty(new KeyAssignmentModel()));
        Assert.False(KeyEditing.IsEmpty(new KeyAssignmentModel { Action = DeckActionType.MediaNext }));
        Assert.False(KeyEditing.IsEmpty(new KeyAssignmentModel { CustomTitle = "x" }));
        Assert.False(KeyEditing.IsEmpty(new KeyAssignmentModel { ImagePath = "a.png" }));
        Assert.False(KeyEditing.IsEmpty(new KeyAssignmentModel
        {
            Gestures = new KeyGestures { Double = new KeyGestureBinding { Action = DeckActionType.MediaNext } }
        }));
        Assert.True(KeyEditing.IsEmpty(new KeyAssignmentModel { Gestures = new KeyGestures() }));
    }

    // ── history ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void History_UndoRedo_WalkBackAndForth()
    {
        var h = new EditHistory<string>();
        h.Record("s0");           // state before edit 1
        h.Record("s1");           // state before edit 2   (current is now "s2")

        Assert.True(h.TryUndo("s2", out var prev));
        Assert.Equal("s1", prev);
        Assert.True(h.TryUndo("s1", out prev));
        Assert.Equal("s0", prev);
        Assert.False(h.TryUndo("s0", out _));

        Assert.True(h.TryRedo("s0", out var next));
        Assert.Equal("s1", next);
        Assert.True(h.TryRedo("s1", out next));
        Assert.Equal("s2", next);
        Assert.False(h.TryRedo("s2", out _));
    }

    [Fact]
    public void History_NewEdit_ForgetsTheRedoBranch()
    {
        var h = new EditHistory<string>();
        h.Record("s0");
        h.TryUndo("s1", out _);
        Assert.True(h.CanRedo);

        h.Record("s0");

        Assert.False(h.CanRedo);
    }

    [Fact]
    public void History_IsLimitedToItsDepth_AndCanBeCleared()
    {
        var h = new EditHistory<int>(depth: 50);
        for (int i = 0; i < 80; i++) h.Record(i);

        Assert.Equal(50, h.UndoCount);
        h.TryUndo(80, out var oldest);
        Assert.Equal(79, oldest);

        h.Clear();
        Assert.False(h.CanUndo);
        Assert.False(h.CanRedo);
    }

    [Fact]
    public void RapidPastes_EachOneIsRecorded_AndUndoneOneByOne()
    {
        // a known bug: pasting several times quickly lost pastes
        var keys = Keys();
        var h = new EditHistory<string>();
        var clipboard = KeyEditing.Clone(keys[1], 0);

        foreach (int target in new[] { 3, 4, 5, 6 })
        {
            h.Record(string.Join(",", keys.Select(k => k.Action)));
            KeyEditing.Paste(keys, target, clipboard);
        }

        Assert.All(new[] { 3, 4, 5, 6 }, i => Assert.Equal(DeckActionType.MediaPlayPause, keys[i].Action));
        Assert.Equal(4, h.UndoCount);
    }
}
