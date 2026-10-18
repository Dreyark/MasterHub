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
                if (item.KeyAction == DeckActionType.None)
                {
                    Canvas.SelectedKey.ClearAction();
                }
                else
                {
                    Canvas.SelectedKey.AssignedAction = item.KeyAction;
                    Canvas.SelectedKey.ActionParameter = KeyItemViewModel.GetDefaultActionParameter(item.KeyAction);
                    Canvas.SelectedKey.CustomTitle = "";
                    Canvas.SelectedKey.CustomSubtitle = "";
                    Canvas.UpdateKeyMonitoringValue(Canvas.SelectedKey);
                    Canvas.RenderKeyVisual(Canvas.SelectedKey, sendToHardware: true);
                }
                SelectedTabIndex = 1; // Po wybraniu konkretnej akcji przełącz na jej właściwości
            }
            else if (item.TargetManipulator == ManipulatorType.Slider && Canvas.SelectedSlider != null)
            {
                Canvas.SelectedSlider.SetTarget(item.SliderTargetType, item.SliderProcessName, item.Title);
                SelectedTabIndex = 1;
            }
            else if (item.TargetManipulator == ManipulatorType.Roller && Canvas.SelectedRoller != null)
            {
                Canvas.SelectedRoller.AssignedAction = item.RollerAction;
                SelectedTabIndex = 1;
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
        else
        {
            IsActionLibraryVisible = true;
            ActionLibrary.SetManipulator(ManipulatorType.Key);
        }
    }
}
