using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Editing;

/// <summary>
/// The pure operations behind the Studio's key editing (T-06): copy/paste, move/swap and "paste look". They work on the
/// saved form of a view's keys (<see cref="KeyAssignmentModel"/>), so they are testable without any UI.
/// </summary>
public static class KeyEditing
{
    private static readonly JsonSerializerOptions Json = new();

    /// <summary>Deep copy of a key, placed at <paramref name="newIndex"/>.</summary>
    public static KeyAssignmentModel Clone(KeyAssignmentModel source, int newIndex)
    {
        var copy = JsonSerializer.Deserialize<KeyAssignmentModel>(JsonSerializer.Serialize(source, Json), Json)!;
        copy.Index = newIndex;
        return copy;
    }

    /// <summary>True for a key with nothing on it (no action, gestures, label or picture): a move onto it does not need a swap.</summary>
    public static bool IsEmpty(KeyAssignmentModel key) =>
        key.Action == DeckActionType.None &&
        (key.Gestures?.IsEmpty ?? true) &&
        string.IsNullOrEmpty(key.CustomTitle) &&
        string.IsNullOrEmpty(key.CustomSubtitle) &&
        string.IsNullOrEmpty(key.ImagePath) &&
        string.IsNullOrEmpty(key.ActiveImagePath);

    /// <summary>Replaces key <paramref name="index"/> with a copy of <paramref name="source"/> (everything, action included).</summary>
    public static void Paste(IList<KeyAssignmentModel> keys, int index, KeyAssignmentModel source)
    {
        Set(keys, index, Clone(source, index));
    }

    /// <summary>Empties a key (it keeps only its index).</summary>
    public static void Clear(IList<KeyAssignmentModel> keys, int index)
    {
        Set(keys, index, new KeyAssignmentModel { Index = index });
    }

    /// <summary>
    /// Drag from <paramref name="from"/> onto <paramref name="to"/>: onto an empty key it is a <b>move</b> (the source
    /// becomes empty), onto an occupied key a <b>swap</b>. Returns <c>false</c> when nothing changes.
    /// </summary>
    public static bool Move(IList<KeyAssignmentModel> keys, int from, int to)
    {
        if (from == to) return false;
        var source = Get(keys, from);
        var target = Get(keys, to);

        Set(keys, to, Clone(source, to));
        Set(keys, from, IsEmpty(target) ? new KeyAssignmentModel { Index = from } : Clone(target, from));
        return true;
    }

    /// <summary>Exchanges two keys.</summary>
    public static void Swap(IList<KeyAssignmentModel> keys, int a, int b)
    {
        if (a == b) return;
        var ka = Get(keys, a);
        var kb = Get(keys, b);
        Set(keys, a, Clone(kb, a));
        Set(keys, b, Clone(ka, b));
    }

    /// <summary>
    /// "Paste look": copies the <b>appearance</b> only – colours, typography, picture fit/zoom/pan and the state pictures –
    /// and never the action, its parameter, gestures, press mode or the label text.
    /// </summary>
    public static void PasteLook(KeyAssignmentModel from, KeyAssignmentModel to)
    {
        to.BackgroundColorHex = from.BackgroundColorHex;
        to.TextColorHex = from.TextColorHex;
        to.SubtitleColorHex = from.SubtitleColorHex;
        to.ActiveBackgroundColorHex = from.ActiveBackgroundColorHex;
        to.ActiveTextColorHex = from.ActiveTextColorHex;
        to.ActiveSubtitleColorHex = from.ActiveSubtitleColorHex;

        to.ImagePath = from.ImagePath;
        to.ActiveImagePath = from.ActiveImagePath;
        to.ImageFitMode = from.ImageFitMode;
        to.ImageScale = from.ImageScale;
        to.ImageOffsetX = from.ImageOffsetX;
        to.ImageOffsetY = from.ImageOffsetY;
        to.ActiveImageFitMode = from.ActiveImageFitMode;
        to.ActiveImageScale = from.ActiveImageScale;
        to.ActiveImageOffsetX = from.ActiveImageOffsetX;
        to.ActiveImageOffsetY = from.ActiveImageOffsetY;

        to.FontFamily = from.FontFamily;
        to.FontSize = from.FontSize;
        to.IsBold = from.IsBold;
        to.IsItalic = from.IsItalic;
        to.IsStrikeThrough = from.IsStrikeThrough;
        to.IsTextWrappingEnabled = from.IsTextWrappingEnabled;

        to.TitleFontFamily = from.TitleFontFamily;
        to.TitleFontSize = from.TitleFontSize;
        to.TitleIsBold = from.TitleIsBold;
        to.TitleIsItalic = from.TitleIsItalic;
        to.TitleIsStrikeThrough = from.TitleIsStrikeThrough;
        to.TitleIsTextWrappingEnabled = from.TitleIsTextWrappingEnabled;
        to.TitleAlignment = from.TitleAlignment;
        to.TitleVerticalPosition = from.TitleVerticalPosition;
        to.TitleOffsetX = from.TitleOffsetX;
        to.TitleOffsetY = from.TitleOffsetY;

        to.SubtitleFontFamily = from.SubtitleFontFamily;
        to.SubtitleFontSize = from.SubtitleFontSize;
        to.SubtitleIsBold = from.SubtitleIsBold;
        to.SubtitleIsItalic = from.SubtitleIsItalic;
        to.SubtitleIsStrikeThrough = from.SubtitleIsStrikeThrough;
        to.SubtitleIsTextWrappingEnabled = from.SubtitleIsTextWrappingEnabled;
        to.SubtitleAlignment = from.SubtitleAlignment;
        to.SubtitleVerticalPosition = from.SubtitleVerticalPosition;
        to.SubtitleOffsetX = from.SubtitleOffsetX;
        to.SubtitleOffsetY = from.SubtitleOffsetY;
    }

    /// <summary>Key with this index (created empty when the list has no entry for it).</summary>
    public static KeyAssignmentModel Get(IList<KeyAssignmentModel> keys, int index)
    {
        var key = keys.FirstOrDefault(k => k.Index == index);
        if (key == null)
        {
            key = new KeyAssignmentModel { Index = index };
            keys.Add(key);
        }
        return key;
    }

    private static void Set(IList<KeyAssignmentModel> keys, int index, KeyAssignmentModel replacement)
    {
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i].Index == index)
            {
                keys[i] = replacement;
                return;
            }
        }
        keys.Add(replacement);
    }
}

/// <summary>
/// Undo / redo of snapshots (T-06): a snapshot is whatever the owner needs to restore a state (here the JSON of a view's keys).
/// Depth-limited; recording a new edit forgets the redo branch.
/// </summary>
public sealed class EditHistory<T>
{
    private readonly int _depth;
    private readonly List<T> _undo = new();
    private readonly List<T> _redo = new();

    public EditHistory(int depth = 50) => _depth = Math.Max(1, depth);

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public int UndoCount => _undo.Count;

    /// <summary>Records the state <b>before</b> an edit.</summary>
    public void Record(T before)
    {
        _undo.Add(before);
        if (_undo.Count > _depth) _undo.RemoveAt(0);
        _redo.Clear();
    }

    /// <summary>Steps back: returns the state to restore (and remembers <paramref name="current"/> for redo).</summary>
    public bool TryUndo(T current, out T previous)
    {
        if (_undo.Count == 0)
        {
            previous = default!;
            return false;
        }

        previous = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(current);
        return true;
    }

    /// <summary>Steps forward again after an undo.</summary>
    public bool TryRedo(T current, out T next)
    {
        if (_redo.Count == 0)
        {
            next = default!;
            return false;
        }

        next = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(current);
        if (_undo.Count > _depth) _undo.RemoveAt(0);
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
