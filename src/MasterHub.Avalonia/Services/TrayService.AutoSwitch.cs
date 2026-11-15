using Avalonia.Controls;
using Avalonia.Threading;
using MasterHub.Core.Localization;

namespace MasterHub.Avalonia.Services;

public sealed partial class TrayService
{
    /// <summary>Tray entry "Auto-switch profiles" (the same global switch as on the Profiles page); a check mark shows it is on.</summary>
    private void AddAutoSwitchItem(NativeMenu menu)
    {
        if (_smart == null) return;

        string text = LocalizationService.Instance.GetString("Tray_AutoSwitch", "Auto-switch profiles");

        var item = new NativeMenuItem(text) { ToggleType = MenuItemToggleType.CheckBox, IsChecked = _smart.IsEnabled };
        item.Click += (_, _) => _smart.IsEnabled = !_smart.IsEnabled;
        _smart.IsEnabledChanged += enabled => Dispatcher.UIThread.Post(() => item.IsChecked = enabled);
        menu.Add(item);
    }
}
