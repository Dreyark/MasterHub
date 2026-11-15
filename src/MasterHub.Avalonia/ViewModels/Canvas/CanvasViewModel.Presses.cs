using MasterHub.Core.Actions;
using MasterHub.Core.Profiles.Auto;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Press counting (T-37): one number per key, only when the user turned it on, kept on this PC.</summary>
public partial class CanvasViewModel
{
    private IPressCounter? _pressCounter;

    /// <summary>The counter that is told about each real press. <c>null</c> = nothing is counted.</summary>
    public IPressCounter? Presses
    {
        get => _pressCounter;
        set => _pressCounter = value;
    }

    /// <summary>
    /// Counts one physical press of <paramref name="key"/> (any gesture of it). A key with nothing assigned and a press in a
    /// preview (dry-run) are not "use", so they are not counted. The counter itself ignores everything while counting is off.
    /// </summary>
    private void CountPress(KeyItemViewModel key)
    {
        var counter = _pressCounter;
        if (counter == null || IsDryRun || key == null || key.Index < 0) return;
        if (key.AssignedAction == DeckActionType.None && key.HoldAction == DeckActionType.None && key.DoubleAction == DeckActionType.None) return;

        string? profileId = _loadedProfileId;
        if (string.IsNullOrEmpty(profileId)) return;

        string place = _currentFolderId != null ? "f:" + _currentFolderId : "p:" + _currentPageIndex;
        counter.Count(profileId, PressCounter.KeyId(place, key.Index));
    }
}
