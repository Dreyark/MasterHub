using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;

namespace MasterHub.Avalonia.Views.Canvas;

/// <summary>Drag to move / swap and the right-click menu of a key (T-06). The Studio-wide shortcuts live in <c>MainWindow.Shortcuts.cs</c>.</summary>
public partial class KeyItemView
{
    private const string DragPrefix = "mhkey:";
    private const double DragThreshold = 8;

    private PointerPressedEventArgs? _dragPress;
    private Point _dragStart;
    private bool _dragging;

    private void InitializeEditing()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnKeyDragOver);
        AddHandler(DragDrop.DropEvent, OnKeyDrop);
        AddHandler(PointerMovedEvent, OnKeyPointerMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, (_, _) => _dragPress = null, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>Called from the pointer-press handler: remembers where a drag could start.</summary>
    private void BeginDrag(PointerPressedEventArgs e)
    {
        _dragPress = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed ? e : null;
        _dragStart = e.GetPosition(this);
    }

    private async void OnKeyPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPress == null || _dragging || DataContext is not KeyItemViewModel key) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { _dragPress = null; return; }

        var delta = e.GetPosition(this) - _dragStart;
        if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold) return;

        var press = _dragPress;
        _dragPress = null;
        _dragging = true;
        try
        {
            var data = new DataTransfer();
            data.Add(DataTransferItem.CreateText(DragPrefix + key.Index));
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move);
        }
        catch
        {
            // a failed drag is just a cancelled drag
        }
        finally
        {
            _dragging = false;
        }
    }

    private static int? ReadDraggedIndex(DragEventArgs e)
    {
        string? text = e.DataTransfer.TryGetText();
        return text != null && text.StartsWith(DragPrefix) && int.TryParse(text[DragPrefix.Length..], out int i) ? i : null;
    }

    private void OnKeyDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = ReadDraggedIndex(e) != null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnKeyDrop(object? sender, DragEventArgs e)
    {
        if (ReadDraggedIndex(e) is int from && DataContext is KeyItemViewModel target)
        {
            FindCanvas()?.MoveKey(from, target.Index);
        }
        e.Handled = true;
    }

    // ── context menu ────────────────────────────────────────────────────────────────────────

    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var canvas = FindCanvas();
        if (canvas == null || sender is not ContextMenu menu) { e.Cancel = true; return; }

        // The menu works on the key under the pointer: make it the selected one first.
        if (DataContext is KeyItemViewModel key) canvas.SelectKey(key);

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Name switch
            {
                "MenuPaste" or "MenuPasteLook" => canvas.HasKeyClipboard,
                "MenuUndo" => canvas.CanUndoEdit,
                "MenuRedo" => canvas.CanRedoEdit,
                _ => true
            };
        }
    }

    private void OnMenuCopy(object? sender, RoutedEventArgs e) => FindCanvas()?.CopyKey(Key());
    private void OnMenuCut(object? sender, RoutedEventArgs e) => FindCanvas()?.CutKey(Key());
    private void OnMenuPaste(object? sender, RoutedEventArgs e) => FindCanvas()?.PasteKey(Key());
    private void OnMenuPasteLook(object? sender, RoutedEventArgs e) => FindCanvas()?.PasteLook(Key());
    private void OnMenuClear(object? sender, RoutedEventArgs e) => FindCanvas()?.ClearKey(Key());
    private void OnMenuClearAll(object? sender, RoutedEventArgs e) => _ = FindCanvas()?.ClearAllKeysAsync();
    private void OnMenuUndo(object? sender, RoutedEventArgs e) => FindCanvas()?.UndoEdit();
    private void OnMenuRedo(object? sender, RoutedEventArgs e) => FindCanvas()?.RedoEdit();

    private KeyItemViewModel? Key() => DataContext as KeyItemViewModel;

    private CanvasViewModel? FindCanvas()
    {
        for (Visual? current = this; current != null; current = current.GetVisualParent())
        {
            if (current is Control { DataContext: CanvasViewModel vm }) return vm;
        }
        return null;
    }
}
