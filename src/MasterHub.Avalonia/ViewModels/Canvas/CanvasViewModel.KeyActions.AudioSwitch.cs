using System.Linq;
using System.Threading.Tasks;
using MasterHub.Core.Audio;

namespace MasterHub.Avalonia.ViewModels.Canvas;

public partial class CanvasViewModel
{
    /// <summary>
    /// One press of an "audio device switch" key: toggles the Windows default device between the key's two devices.
    /// The decision is <see cref="AudioSwitchPlanner"/> (pure); a missing device is reported on the key instead of throwing.
    /// The COM call runs off the UI thread. The volume knob and mute keys resolve the default device on every call, so they
    /// follow the switch without any re-binding (B-19).
    /// </summary>
    private void SwitchAudioDevice(KeyItemViewModel key)
    {
        var service = _audioDevices;
        if (service == null) return;

        var parameter = AudioSwitchParameter.Parse(key.ActionParameter);
        var devices = service.GetAllDevices();
        var plan = AudioSwitchPlanner.Plan(devices, parameter);

        if (!plan.CanSwitch)
        {
            System.Diagnostics.Debug.WriteLine($"[audio switch] key {key.Index + 1}: {plan.Problem}");
            return;
        }

        var target = plan.Target!;
        var a = AudioDeviceMatcher.Resolve(devices.Where(d => d.IsCapture == parameter.Capture).ToList(), parameter.A);
        bool targetIsFirst = a != null && a.Id == target.Id;

        _ = Task.Run(() =>
        {
            if (!service.SetDefault(target.Id, out string error))
            {
                System.Diagnostics.Debug.WriteLine($"[audio switch] could not set '{target.Name}': {error}");
                return;
            }

            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                key.IsActiveToggleState = targetIsFirst;
                RenderKeyVisual(key, sendToHardware: true);
            });
        });
    }
}
