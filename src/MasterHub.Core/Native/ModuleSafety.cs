using System;

namespace MasterHub.Core.Native;

/// <summary>
/// Software-mode bookkeeping of one module and the rules derived from it.
/// <para>
/// Firmware bug (Key Deck 15 and Dial, verified on hardware): reading <b>any</b> feature
/// report while the module is in hardware mode wedges it until its power is cycled (every later read comes back
/// empty). Faders, knobs, rollers and the base (the "M0 family") answer reads at any time.
/// MasterHub does not read features from keys/dial today, but the transport enforces the rule so that a future
/// diagnostic read can never wedge a module.
/// </para>
/// <para>After <see cref="MarkReleased"/> nothing may be sent to the module any more (the module has been handed back).</para>
/// </summary>
public sealed class ModuleSoftwareState
{
    private volatile bool _softwareMode;
    private volatile bool _released;

    public ModuleSoftwareState(ushort productId)
    {
        RequiresSoftwareModeForReads = RequiresSoftwareMode(productId);
    }

    /// <summary>True for Key Deck 15 (0x01FE) and Dial (0x0206).</summary>
    public static bool RequiresSoftwareMode(ushort productId) => productId is 0x01FE or 0x0206;

    public bool RequiresSoftwareModeForReads { get; }

    public bool IsSoftwareMode => _softwareMode;

    public bool IsReleased => _released;

    /// <summary>Whether a feature report may be read right now.</summary>
    public bool MayReadFeatures => !_released && (!RequiresSoftwareModeForReads || _softwareMode);

    /// <summary>Records the outcome of a software-mode command (only a successful send changes the state).</summary>
    public void OnSoftwareModeSent(bool enabled, int result)
    {
        if (result == 0) _softwareMode = enabled;
    }

    public void MarkReleased()
    {
        _released = true;
        _softwareMode = false;
    }
}

/// <summary>What the handshake needs from a module (implemented by an adapter around a HID handle, or by a fake in tests).</summary>
public interface IModuleHandshakeLink
{
    /// <summary>Sends the software-mode command and returns the HID result (0 = success).</summary>
    int SetSoftwareMode(bool enabled);

    /// <summary>
    /// Optional confirmation read after the second send. Return <c>null</c> when no verified read-back exists for this
    /// firmware (the handshake then trusts the accepted sends), <c>true</c>/<c>false</c> for a confirmed/failed read.
    /// Only ever called after both sends succeeded and the module is in software mode.
    /// </summary>
    bool? Confirm();
}

/// <summary>Handshake adapter over a real HID handle (Key Deck 15 or Dial).</summary>
public sealed class HandleHandshakeLink : IModuleHandshakeLink
{
    private readonly MasterHubDeviceHandle _handle;
    private readonly bool _isDial;

    public HandleHandshakeLink(MasterHubDeviceHandle handle, bool isDial)
    {
        _handle = handle;
        _isDial = isDial;
    }

    public int SetSoftwareMode(bool enabled) => _isDial ? _handle.SetDialSwMode(enabled) : _handle.SetSwMode(enabled);

    /// <summary>
    /// No feature read-back is verified for our firmware (docs/HARDWARE_PROTOCOL.md lists none for keys/dial), and a
    /// wrong read id is exactly what this task guards against – so the accepted sends are trusted.
    /// Plug a verified read in here once T-29 has validated one on hardware.
    /// </summary>
    public bool? Confirm() => null;
}

/// <summary>Outcome of <see cref="SoftwareModeHandshake.Run"/>.</summary>
public readonly record struct HandshakeResult(bool Ready, int Attempts);

/// <summary>
/// Reliable "enter software mode" sequence of Key Deck 15 / Dial (as found necessary on real hardware: a keys module
/// once accepted every command but answered nothing): software mode → 600 ms settle → software mode again → 200 ms →
/// confirmation. A send that fails is <b>never</b> followed by a read. Up to 3 attempts; between attempts the module
/// is put back to hardware mode and given 500 ms. When all attempts fail the module is reported as unresponsive.
/// </summary>
public static class SoftwareModeHandshake
{
    public const int MaxAttempts = 3;
    public const int SettleMs = 600;
    public const int SecondSettleMs = 200;
    public const int RetryPauseMs = 500;

    public static HandshakeResult Run(IModuleHandshakeLink link, Action<int> sleep, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(sleep);

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (TryOnce(link, sleep, log, attempt))
            {
                return new HandshakeResult(true, attempt);
            }

            if (attempt < MaxAttempts)
            {
                try { link.SetSoftwareMode(false); } catch { /* the module may already be gone */ }
                sleep(RetryPauseMs);
            }
        }

        return new HandshakeResult(false, MaxAttempts);
    }

    private static bool TryOnce(IModuleHandshakeLink link, Action<int> sleep, Action<string>? log, int attempt)
    {
        int first = link.SetSoftwareMode(true);
        if (first != 0)
        {
            log?.Invoke($"software mode, attempt {attempt}: first send failed ({first})");
            return false;
        }

        sleep(SettleMs);

        int second = link.SetSoftwareMode(true);
        if (second != 0)
        {
            log?.Invoke($"software mode, attempt {attempt}: second send failed ({second})");
            return false;
        }

        sleep(SecondSettleMs);

        bool? confirmed = link.Confirm();
        if (confirmed == false)
        {
            log?.Invoke($"software mode, attempt {attempt}: module did not answer the confirmation read");
            return false;
        }

        return true;
    }
}
