using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Inspector;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Actions;

namespace MasterHub.UiTests;

/// <summary>
/// The action of Press / Hold / Double is chosen in the Action library; the properties only show it and hold its settings
/// (no flat lists of ~300 actions in the properties).
/// </summary>
public class ActionSlotTests
{
    private static (InspectorViewModel inspector, KeyItemViewModel key) Rig()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mh_slot_" + Guid.NewGuid().ToString("N"));
        var main = StudioShotTests.BuildMain(tmp, MasterHub.Avalonia.Services.NavigationPage.Studio);
        var key = main.Canvas.Keys[4];
        main.Canvas.SelectKey(key);
        return (main.Inspector, key);
    }

    private static ActionCatalogItem Item(DeckActionType a) => new("x", "c", "i", ManipulatorType.Key, a);

    [AvaloniaFact]
    public void TheSlotShowsWhatItDoes_ForPressHoldAndDouble()
    {
        var (_, key) = Rig();
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.AssignGestureAction(1, DeckActionType.MediaNext);

        key.GestureTab = 0;
        Assert.Equal(DeckActionType.MediaPlayPause, key.ActiveSlotAction);
        key.GestureTab = 1;
        Assert.Equal(DeckActionType.MediaNext, key.ActiveSlotAction);
        Assert.True(key.HasActiveSlotAction);
        key.GestureTab = 2;
        Assert.Equal(DeckActionType.None, key.ActiveSlotAction);
        Assert.False(key.HasActiveSlotAction);
    }

    [AvaloniaFact]
    public void ChoosingInTheLibrary_WithTheHoldSlotSelected_SetsHold_AndLeavesPressAlone()
    {
        var (inspector, key) = Rig();
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.GestureTab = 1;

        inspector.ActionLibrary.ChooseAction(Item(DeckActionType.MediaNext));

        Assert.Equal(DeckActionType.MediaNext, key.HoldAction);
        Assert.Equal(DeckActionType.MediaPlayPause, key.AssignedAction);
        Assert.Equal(1, inspector.SelectedTabIndex);   // back to the properties to set it up
    }

    [AvaloniaFact]
    public void ChoosingInTheLibrary_WithPressSelected_StillAssignsThePressAction()
    {
        var (inspector, key) = Rig();
        key.GestureTab = 0;

        inspector.ActionLibrary.ChooseAction(Item(DeckActionType.MediaNext));

        Assert.Equal(DeckActionType.MediaNext, key.AssignedAction);
        Assert.Equal(DeckActionType.None, key.HoldAction);
    }

    [AvaloniaFact]
    public void APluginAction_IsRefusedForHold_WithAnExplanation()
    {
        var (inspector, key) = Rig();
        key.GestureTab = 1;

        inspector.ActionLibrary.ChooseAction(Item(DeckActionType.StreamDeckPluginAction));

        Assert.Equal(DeckActionType.None, key.HoldAction);
        Assert.True(inspector.ActionLibrary.HasSlotNotice);
    }

    [AvaloniaFact]
    public void TheLibrary_SaysWhichSlotItIsChoosingFor_AndTheWayBackToPress()
    {
        var (inspector, key) = Rig();
        Assert.False(inspector.ActionLibrary.IsSlotBannerVisible);

        key.GestureTab = 2;
        inspector.OpenActionLibrary();

        Assert.True(inspector.ActionLibrary.IsSlotBannerVisible);
        Assert.Contains("5", inspector.ActionLibrary.SlotBannerText);
        Assert.Equal(0, inspector.SelectedTabIndex);

        inspector.ActionLibrary.ReturnToPressCommand.Execute(null);
        Assert.Equal(0, key.GestureTab);
        Assert.False(inspector.ActionLibrary.IsSlotBannerVisible);
    }

    [AvaloniaFact]
    public void ClearingTheSlot_EmptiesHoldButNotPress()
    {
        var (_, key) = Rig();
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.AssignGestureAction(1, DeckActionType.MediaNext);
        key.GestureTab = 1;

        key.ClearActiveSlotCommand.Execute(null);

        Assert.Equal(DeckActionType.None, key.HoldAction);
        Assert.Equal(DeckActionType.MediaPlayPause, key.AssignedAction);
    }

    [AvaloniaFact]
    public void ThePropertiesNoLongerHoldAFlatListOfActions()
    {
        var view = new KeyGestureTabsView { DataContext = new KeyItemViewModel { Index = 0 } };
        var window = new global::Avalonia.Controls.Window { Content = view, Width = 340, Height = 400 };
        window.Show();

        var combos = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window).OfType<global::Avalonia.Controls.ComboBox>();
        Assert.Empty(combos);
        window.Close();
    }
}

public class ActionSlotShotTests
{
    [AvaloniaFact]
    public void Shots_SlotCardAndLibraryBanner()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "mh_slotshot_" + Guid.NewGuid().ToString("N"));
        var main = StudioShotTests.BuildMain(tmp, MasterHub.Avalonia.Services.NavigationPage.Studio);
        var key = main.Canvas.Keys[4];
        main.Canvas.SelectKey(key);
        key.AssignedAction = DeckActionType.MediaPlayPause;
        key.AssignGestureAction(1, DeckActionType.MediaNext);
        key.GestureTab = 1;
        main.Inspector.OpenActionLibrary();

        Shot.Render(new KeyInspectorView { DataContext = key }, "slot_properties_hold", 340, 560);
        Shot.Render(new ActionLibraryView { DataContext = main.Inspector.ActionLibrary }, "slot_library_banner", 340, 560);
    }
}
