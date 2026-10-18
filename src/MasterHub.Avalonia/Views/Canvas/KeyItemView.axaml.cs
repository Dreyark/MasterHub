using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Actions;

namespace MasterHub.Avalonia.Views.Canvas;

public partial class KeyItemView : UserControl
{
    private static DateTime _lastClickTime = DateTime.MinValue;
    private static int _lastClickedIndex = -1;

    public KeyItemView()
    {
        InitializeComponent();
    }

    private void OnKeyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is KeyItemViewModel key)
        {
            e.Handled = true;
            var canvasVm = this.FindAncestorOfType<KeysModuleView>()?.DataContext as CanvasViewModel
                        ?? this.FindAncestorOfType<ModuleCanvasView>()?.DataContext as CanvasViewModel;

            if (canvasVm == null)
            {
                var current = this.GetVisualParent();
                while (current != null)
                {
                    if (current is Control control && control.DataContext is CanvasViewModel vm)
                    {
                        canvasVm = vm;
                        break;
                    }
                    current = current.GetVisualParent();
                }
            }

            if (canvasVm != null)
            {
                bool isDoubleClick = e.ClickCount >= 2 ||
                    ((DateTime.UtcNow - _lastClickTime).TotalMilliseconds < 500 && _lastClickedIndex == key.Index);

                _lastClickTime = DateTime.UtcNow;
                _lastClickedIndex = key.Index;

                if (isDoubleClick)
                {
                    canvasVm.DeselectAll();
                    if (key.AssignedAction == DeckActionType.OpenFolder)
                    {
                        string folder = !string.IsNullOrWhiteSpace(key.ActionParameter) ? key.ActionParameter : "Nowy Folder";
                        canvasVm.OpenFolder(folder);
                        return;
                    }
                    if (key.AssignedAction == DeckActionType.GoBack)
                    {
                        canvasVm.NavigateBack();
                        return;
                    }
                    if (key.AssignedAction == DeckActionType.GoHome)
                    {
                        canvasVm.NavigateHome();
                        return;
                    }
                    if (key.AssignedAction == DeckActionType.GoToPage)
                    {
                        canvasVm.SwitchToPage(canvasVm.ParsePageIndex(key.ActionParameter));
                        return;
                    }
                    if (key.AssignedAction == DeckActionType.NextPage)
                    {
                        canvasVm.NextPage();
                        return;
                    }
                    if (key.AssignedAction == DeckActionType.PreviousPage)
                    {
                        canvasVm.PreviousPage();
                        return;
                    }
                    if (key.AssignedAction != DeckActionType.None)
                    {
                        canvasVm.ExecuteKeyAction(key);
                        return;
                    }
                }

                canvasVm.SelectKey(key);
            }
        }
    }
}
