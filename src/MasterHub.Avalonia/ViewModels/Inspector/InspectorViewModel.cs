using CommunityToolkit.Mvvm.ComponentModel;
using MasterHub.Avalonia.ViewModels.Canvas;
using MasterHub.Core.Actions;

namespace MasterHub.Avalonia.ViewModels.Inspector;

/// <summary>
/// Główny ViewModel prawego panelu inspektora koordynujący stan nagłówka, dynamiczną bibliotekę akcji oraz właściwości zaznaczonego manipulatora.
/// </summary>
public partial class InspectorViewModel : ObservableObject, System.IDisposable
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

    private readonly System.ComponentModel.PropertyChangedEventHandler _onCanvasPropertyChanged;

    public InspectorViewModel(
        DeviceHeaderViewModel header,
        ActionLibraryViewModel actionLibrary,
        CanvasViewModel canvas)
    {
        _header = header;
        _actionLibrary = actionLibrary;
        _canvas = canvas;

        _onCanvasPropertyChanged = (s, e) =>
        {
            if (e.PropertyName == nameof(CanvasViewModel.SelectedElement))
            {
                UpdateSelectionState();
            }
        };
        Canvas.PropertyChanged += _onCanvasPropertyChanged;

        UpdateSelectionState();
        ActionLibrary.BackToPress = () => { if (Canvas.SelectedKey != null) Canvas.SelectedKey.GestureTab = 0; };

        ActionLibrary.ActionSelected = item =>
        {
            ActionLibrary.SlotNotice = "";
            if (item.TargetManipulator == ManipulatorType.Key && Canvas.SelectedKey is { GestureTab: not 0 } slotKey)
            {
                // the Hold / Double slot is the target: that slot gets the action, the key's tap action stays as it is
                if (!slotKey.AssignGestureAction(slotKey.GestureTab, item.KeyAction))
                {
                    ActionLibrary.SlotNotice = MasterHub.Core.Localization.LocalizationService.Instance.GetString(
                        "Library_SlotNoPlugin", "A plugin action can only be used for Press. Switch the target back to Press.");
                    return;
                }
                SelectedTabIndex = 1;
                return;
            }

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
                        Canvas.SelectedKey.StreamDeckSettingsJson = "{}";
                        Canvas.SelectedKey.SetPluginDynamicImage(null);
                        if (!string.IsNullOrEmpty(item.IconImagePath))
                        {
                            Canvas.SelectedKey.ImagePath = item.IconImagePath;
                        }
                    }
                    else
                    {
                        if (Canvas.SelectedKey.ImagePath?.Contains(".sdPlugin", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            Canvas.SelectedKey.ImagePath = "";
                        }
                    }
                    Canvas.SelectedKey.SelectedEditState = 0;
                    Canvas.SelectedKey.ActionParameter = KeyItemViewModel.GetDefaultActionParameter(item.KeyAction);
                    Canvas.SelectedKey.CustomTitle = item.Title ?? "";
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

    /// <summary>"Change…" in the properties: opens the Action library for the slot being edited.</summary>
    public void OpenActionLibrary()
    {
        ActionLibrary.SlotNotice = "";
        UpdateSlotBanner();
        SelectedTabIndex = 0;
    }

    private KeyItemViewModel? _watchedKey;

    private void WatchSelectedKey()
    {
        if (ReferenceEquals(_watchedKey, Canvas.SelectedKey)) return;
        if (_watchedKey != null) _watchedKey.PropertyChanged -= OnWatchedKeyChanged;
        _watchedKey = Canvas.SelectedKey;
        if (_watchedKey != null) _watchedKey.PropertyChanged += OnWatchedKeyChanged;
    }

    private void OnWatchedKeyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(KeyItemViewModel.GestureTab)) UpdateSlotBanner();
    }

    /// <summary>The library says which slot it is choosing for while that is Hold or Double.</summary>
    private void UpdateSlotBanner()
    {
        var key = Canvas.SelectedKey;
        ActionLibrary.SlotNotice = "";
        if (key == null || key.GestureTab == 0)
        {
            ActionLibrary.SlotBannerText = "";
            return;
        }

        var loc = MasterHub.Core.Localization.LocalizationService.Instance;
        string slot = key.GestureTab == 1 ? loc.GetString("Gesture_Hold", "Hold") : loc.GetString("Gesture_Double", "Double");
        ActionLibrary.SlotBannerText = string.Format(loc.GetString("Library_SlotBanner", "Choosing the action for: {0} · key {1}"), slot, key.Index + 1);
    }

    private void UpdateSelectionState()
    {
        WatchSelectedKey();
        UpdateSlotBanner();
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
            SelectedTabIndex = Canvas.SelectedKey.HasAction ? 1 : 0;
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

    public void Dispose()
    {
        Canvas.PropertyChanged -= _onCanvasPropertyChanged;
        if (_watchedKey != null) _watchedKey.PropertyChanged -= OnWatchedKeyChanged;
        ActionLibrary.Dispose();
    }
}
