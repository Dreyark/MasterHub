using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MasterHub.Core.Editing;
using MasterHub.Core.Profiles;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>
/// Key editing ergonomics (T-06): copy / cut / paste / clear, drag to move or swap, "paste look" and undo / redo.
/// Every edit is a change of the saved keys of the current view (page or folder), recorded as a snapshot first, so undo
/// is exact. The history is per view and per profile (it is cleared when either changes). The operations run one after
/// another on the UI thread, so rapid repeated pastes cannot lose each other.
/// </summary>
public partial class CanvasViewModel
{
    private readonly EditHistory<string> _editHistory = new(50);
    private string _historyView = "";
    private KeyAssignmentModel? _keyClipboard;

    public bool HasKeyClipboard => _keyClipboard != null;
    public bool CanUndoEdit => _editHistory.CanUndo;
    public bool CanRedoEdit => _editHistory.CanRedo;

    /// <summary>Forgets undo/redo (a profile was applied or switched).</summary>
    public void ClearEditHistory()
    {
        _editHistory.Clear();
        _historyView = "";
        NotifyEditState();
    }

    public void CopyKey(KeyItemViewModel? key = null)
    {
        key ??= SelectedKey;
        if (key == null) return;

        var models = BuildKeyAssignments();
        _keyClipboard = KeyEditing.Clone(KeyEditing.Get(models, key.Index), key.Index);
        NotifyEditState();
    }

    public void CutKey(KeyItemViewModel? key = null)
    {
        key ??= SelectedKey;
        if (key == null || !CanEditKey(key.Index)) return;

        CopyKey(key);
        ClearKey(key);
    }

    public void PasteKey(KeyItemViewModel? target = null)
    {
        target ??= SelectedKey;
        if (target == null || _keyClipboard == null || !CanEditKey(target.Index)) return;

        var clipboard = _keyClipboard;
        Edit(models => KeyEditing.Paste(models, target.Index, clipboard), target.Index);
    }

    /// <summary>Applies only the appearance of the copied key (colours, typography, picture fit) and keeps the target's action.</summary>
    public void PasteLook(KeyItemViewModel? target = null)
    {
        target ??= SelectedKey;
        if (target == null || _keyClipboard == null || !CanEditKey(target.Index)) return;

        var clipboard = _keyClipboard;
        Edit(models => KeyEditing.PasteLook(clipboard, KeyEditing.Get(models, target.Index)), target.Index);
    }

    public void ClearKey(KeyItemViewModel? key = null)
    {
        key ??= SelectedKey;
        if (key == null || !CanEditKey(key.Index)) return;
        if (KeyEditing.IsEmpty(KeyEditing.Get(BuildKeyAssignments(), key.Index))) return; // already empty: no history entry

        Edit(models => KeyEditing.Clear(models, key.Index), key.Index);
    }

    /// <summary>
    /// Clears every key of the page (or folder) shown, after an in-app confirmation whose button says what happens (T-03).
    /// One undo step brings them all back. The folder's "back" key stays. Returns <c>true</c> when the keys were cleared.
    /// </summary>
    public async System.Threading.Tasks.Task<bool> ClearAllKeysAsync()
    {
        var models = BuildKeyAssignments();
        var toClear = Enumerable.Range(0, Keys.Count)
            .Where(i => CanEditKey(i) && !KeyEditing.IsEmpty(KeyEditing.Get(models, i)))
            .ToList();
        if (toClear.Count == 0) return false;

        var loc = MasterHub.Core.Localization.LocalizationService.Instance;
        var request = new MasterHub.Core.Dialogs.ConfirmRequest(
            loc.GetString("Confirm_ClearAllKeys_Title", "Clear all keys on this page?"),
            string.Format(loc.GetString("Confirm_ClearAllKeys_Message", "{0} keys lose their action and look. Ctrl+Z brings them back."), toClear.Count),
            loc.GetString("Confirm_ClearAllKeys_Button", "Clear all keys"),
            loc.GetString("Confirm_Cancel", "Cancel"));
        if (!await _confirmDialog.ConfirmAsync(request, System.TimeSpan.FromSeconds(30))) return false;

        Edit(list =>
        {
            foreach (int i in toClear) KeyEditing.Clear(list, i);
        });
        return true;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    private async System.Threading.Tasks.Task ClearAllKeys() => await ClearAllKeysAsync();

    /// <summary>Drag and drop: onto an empty key it moves, onto an occupied key it swaps.</summary>
    public void MoveKey(int from, int to)
    {
        if (from == to || !CanEditKey(from) || !CanEditKey(to)) return;
        if (from < 0 || to < 0 || from >= Keys.Count || to >= Keys.Count) return;

        Edit(models => KeyEditing.Move(models, from, to), to);
    }

    public void UndoEdit()
    {
        EnsureHistoryView();
        if (_editHistory.TryUndo(Snapshot(), out var previous)) Restore(previous);
    }

    public void RedoEdit()
    {
        EnsureHistoryView();
        if (_editHistory.TryRedo(Snapshot(), out var next)) Restore(next);
    }

    // ── plumbing ────────────────────────────────────────────────────────────────────────────

    /// <summary>Inside a folder key 1 is the locked "back" key.</summary>
    private bool CanEditKey(int index) => !(IsInsideFolder && index == 0);

    private string CurrentViewId() =>
        (IsInsideFolder ? $"folder:{CurrentFolderId}" : $"page:{CurrentPageIndex}") + ":" + _profileService.GetActiveProfile().Id;

    private void EnsureHistoryView()
    {
        string view = CurrentViewId();
        if (view == _historyView) return;

        _editHistory.Clear();
        _historyView = view;
        NotifyEditState();
    }

    private string Snapshot() => JsonSerializer.Serialize(BuildKeyAssignments());

    private void Edit(Action<List<KeyAssignmentModel>> mutate, int? selectIndex = null)
    {
        EnsureHistoryView();
        var models = BuildKeyAssignments();
        string before = JsonSerializer.Serialize(models);

        mutate(models);
        if (JsonSerializer.Serialize(models) == before) return; // nothing changed: no history entry

        _editHistory.Record(before);
        ApplyKeyModels(models);
        if (selectIndex is int i && i >= 0 && i < Keys.Count) SelectKey(Keys[i]);
    }

    private void Restore(string snapshot)
    {
        var models = JsonSerializer.Deserialize<List<KeyAssignmentModel>>(snapshot);
        if (models != null) ApplyKeyModels(models);
    }

    /// <summary>Writes keys into the current view of the active profile and reloads the keys from it.</summary>
    private void ApplyKeyModels(List<KeyAssignmentModel> models)
    {
        SaveCurrentStateToActiveProfile(); // flush the rest of the Studio's state first
        var profile = _profileService.GetActiveProfile();
        EnsurePagesInitialized(profile);
        profile.Folders ??= new List<KeyFolderModel>();

        StoreViewKeys(profile, models);
        _profileService.SaveProfile(profile);
        LoadCurrentViewKeys(profile);
        NotifyEditState();
    }

    private void NotifyEditState()
    {
        OnPropertyChanged(nameof(HasKeyClipboard));
        OnPropertyChanged(nameof(CanUndoEdit));
        OnPropertyChanged(nameof(CanRedoEdit));
    }
}
