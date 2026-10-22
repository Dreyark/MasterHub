using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Actions;

namespace MasterHub.Avalonia.ViewModels.Inspector;

/// <summary>
/// Główny ViewModel prawego panelu inspektora koordynujący stan nagłówka, dynamiczną bibliotekę akcji oraz właściwości zaznaczonego manipulatora.
/// </summary>
public partial class InspectorViewModel : ObservableObject
{
    [ObservableProperty]
    private DeviceHeaderViewModel _header;

    [ObservableProperty]
    private ActionLibraryViewModel _actionLibrary;

    [ObservableProperty]
    private CanvasViewModel _canvas;

    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Biblioteka Akcji, 1 = Właściwości Zaznaczenia

    [ObservableProperty]
    private bool _isActionLibraryVisible = true;

    public InspectorViewModel(
        DeviceHeaderViewModel header,
        ActionLibraryViewModel actionLibrary,
        CanvasViewModel canvas)
    {
        _header = header;
        _actionLibrary = actionLibrary;
        _canvas = canvas;

        Canvas.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(CanvasViewModel.SelectedElement))
            {
                UpdateSelectionState();
            }
        };

        UpdateSelectionState();

        ActionLibrary.ActionSelected = item =>
        {
            if (item.TargetManipulator == ManipulatorType.Key && Canvas.SelectedKey != null)
            {
                var prevPluginUuid = Canvas.SelectedKey.StreamDeckPluginUuid;
                var prevActionUuid = Canvas.SelectedKey.StreamDeckActionUuid;
                bool wasPluginAction = Canvas.SelectedKey.AssignedAction == DeckActionType.StreamDeckPluginAction;

                if (item.KeyAction == DeckActionType.None)
                {
                    if (wasPluginAction && !string.IsNullOrEmpty(prevPluginUuid))
                    {
                        _ = Canvas.SendStreamDeckWillDisappearAsync(Canvas.SelectedKey.Index, prevPluginUuid, prevActionUuid);
                    }
                    Canvas.SelectedKey.ClearAction();
                    Canvas.RenderKeyVisual(Canvas.SelectedKey, sendToHardware: true);
                }
                else
                {
                    if (wasPluginAction && !string.IsNullOrEmpty(prevPluginUuid) &&
                        (item.KeyAction != DeckActionType.StreamDeckPluginAction || prevPluginUuid != item.StreamDeckPluginUuid || prevActionUuid != item.StreamDeckActionUuid))
                    {
                        _ = Canvas.SendStreamDeckWillDisappearAsync(Canvas.SelectedKey.Index, prevPluginUuid, prevActionUuid);
                    }

                    Canvas.SelectedKey.AssignedAction = item.KeyAction;
                    if (item.KeyAction == DeckActionType.StreamDeckPluginAction)
                    {
                        Canvas.SelectedKey.StreamDeckPluginUuid = item.StreamDeckPluginUuid ?? "";
                        Canvas.SelectedKey.StreamDeckActionUuid = item.StreamDeckActionUuid ?? "";
                        Canvas.SelectedKey.SetPluginDynamicImage(null);
                        if (!string.IsNullOrEmpty(item.IconImagePath))
                        {
                            Canvas.SelectedKey.ImagePath = item.IconImagePath;
                        }
                    }
                    Canvas.SelectedKey.ActionParameter = KeyItemViewModel.GetDefaultActionParameter(item.KeyAction);
                    Canvas.SelectedKey.CustomTitle = item.KeyAction == DeckActionType.StreamDeckPluginAction ? (item.ShowTitle ? item.Title : "") : "";
                    Canvas.SelectedKey.CustomSubtitle = "";
                    Canvas.SelectedKey.AutoExtractIconIfApplicable();
                    Canvas.UpdateKeyMonitoringValue(Canvas.SelectedKey);
                    Canvas.RenderKeyVisual(Canvas.SelectedKey, sendToHardware: true);

                    if (item.KeyAction == DeckActionType.StreamDeckPluginAction)
                    {
                        _ = Canvas.SendStreamDeckWillAppearAsync(Canvas.SelectedKey);
                    }
                }
                SelectedTabIndex = 1;
            }
            else if (item.TargetManipulator == ManipulatorType.Slider && Canvas.SelectedSlider != null)
            {
                Canvas.SelectedSlider.SetTarget(item.SliderTargetType, item.SliderProcessName, item.Title);
                SelectedTabIndex = 1;
            }
            else if (item.TargetManipulator == ManipulatorType.Roller || item.TargetManipulator == ManipulatorType.Knob || item.TargetManipulator == ManipulatorType.Dial)
            {
                if (Canvas.SelectedRoller != null)
                {
                    Canvas.SelectedRoller.AssignedAction = item.RollerAction;
                    SelectedTabIndex = 1;
                }
                else if (Canvas.SelectedKnob != null)
                {
                    Canvas.SelectedKnob.RotateAction = item.RollerAction;
                    SelectedTabIndex = 1;
                }
                else if (Canvas.SelectedDial != null)
                {
                    Canvas.SelectedDial.RotateAction = item.RollerAction;
                    SelectedTabIndex = 1;
                }
            }
        };
    }

    private void UpdateSelectionState()
    {
        if (Canvas.IsBaseSelected)
        {
            // Podstawa dock jest tylko podstawką z podświetleniem LED - nie jest kontrolerem wejściowym i nie wykonuje akcji.
            // Ukrywamy zakładkę biblioteki akcji i otwieramy bezpośrednio właściwości (kolor i jasność LED).
            IsActionLibraryVisible = false;
            SelectedTabIndex = 1;
        }
        else if (Canvas.SelectedKey != null)
        {
            IsActionLibraryVisible = true;
            ActionLibrary.SetManipulator(ManipulatorType.Key);
            SelectedTabIndex = 0;
        }
        else if (Canvas.SelectedSlider != null)
        {
            IsActionLibraryVisible = true;
            ActionLibrary.SetManipulator(ManipulatorType.Slider);
            SelectedTabIndex = 0;
        }
        else if (Canvas.SelectedRoller != null)
        {
            IsActionLibraryVisible = true;
            ActionLibrary.SetManipulator(ManipulatorType.Roller);
            SelectedTabIndex = 0;
        }
        else if (Canvas.SelectedKnob != null)
        {
            IsActionLibraryVisible = true;
            ActionLibrary.SetManipulator(ManipulatorType.Knob);
            SelectedTabIndex = 0;
        }
        else if (Canvas.SelectedDial != null)
        {
            IsActionLibraryVisible = true;
            ActionLibrary.SetManipulator(ManipulatorType.Dial);
            SelectedTabIndex = 0;
        }
        else
        {
            IsActionLibraryVisible = true;
            ActionLibrary.SetManipulator(ManipulatorType.Key);
        }
    }
}
