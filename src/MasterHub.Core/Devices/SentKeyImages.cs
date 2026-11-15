using System;
using System.Linq;

namespace MasterHub.Core.Devices;

/// <summary>
/// Remembers the last picture each key of the keypad was sent, so a picture that did not change is not sent again (T-30).
/// Writing one key takes tens of milliseconds on the USB link and a redraw is often asked for without a visible change
/// (a state poll, an edit of another field, a look rule that still holds). The comparison is byte for byte, so a changed
/// picture is never skipped; the rotation is part of what is remembered.
/// </summary>
/// <remarks>
/// Whoever writes to the screen by another way (the whole-panel picture, a fill, a (re)connect, standby) must call
/// <see cref="Clear"/> or <see cref="Forget"/>: after that the next picture is sent whatever it is.
/// </remarks>
public sealed class SentKeyImages
{
    private readonly object _lock = new();
    private readonly byte[]?[] _last;
    private readonly int[] _angle;
    private long _sent;
    private long _skipped;

    public SentKeyImages(int keys = 15)
    {
        _last = new byte[keys][];
        _angle = new int[keys];
    }

    /// <summary>Pictures that went to the device since the start.</summary>
    public long Sent { get { lock (_lock) return _sent; } }

    /// <summary>Pictures that were not sent because the key already shows them.</summary>
    public long Skipped { get { lock (_lock) return _skipped; } }

    /// <summary><c>true</c> when the key already shows exactly this picture at this rotation (counted as skipped).</summary>
    public bool IsAlreadyShown(int key, byte[] jpeg, int angle)
    {
        lock (_lock)
        {
            if ((uint)key >= (uint)_last.Length) return false;

            var last = _last[key];
            bool same = last != null && _angle[key] == angle && last.AsSpan().SequenceEqual(jpeg);
            if (same) _skipped++;
            return same;
        }
    }

    /// <summary>The picture reached the key. Only call it after the write succeeded.</summary>
    public void Remember(int key, byte[] jpeg, int angle)
    {
        lock (_lock)
        {
            if ((uint)key >= (uint)_last.Length) return;

            _last[key] = jpeg.ToArray();   // a copy: a caller that reuses its buffer must not change what we compare with
            _angle[key] = angle;
            _sent++;
        }
    }

    /// <summary>The key shows something else now (or may): the next picture is sent.</summary>
    public void Forget(int key)
    {
        lock (_lock)
        {
            if ((uint)key < (uint)_last.Length) _last[key] = null;
        }
    }

    /// <summary>Nothing is known about the screen (connect, standby, whole-panel picture): everything is sent again.</summary>
    public void Clear()
    {
        lock (_lock) Array.Clear(_last);
    }
}
