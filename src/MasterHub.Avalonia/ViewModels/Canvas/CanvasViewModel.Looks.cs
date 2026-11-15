using System;
using Avalonia.Threading;
using MasterHub.Avalonia.Services;
using MasterHub.Core.Looks;
using MasterHub.Core.Monitoring;

namespace MasterHub.Avalonia.ViewModels.Canvas;

/// <summary>Conditional looks and label tokens (T-12): a light timer re-evaluates the rules of the keys on screen; a key is redrawn only when its look changed.</summary>
public partial class CanvasViewModel
{
    private static readonly TimeSpan LookInterval = TimeSpan.FromMilliseconds(500);

    private DispatcherTimer? _lookTimer;
    private ILookSignals? _lookSignals;
    private MasterHub.Core.Status.IStatusBoard? _statusBoard;

    /// <summary>What the conditions read. Tests replace it with a fixed set of values.</summary>
    internal ILookSignals LookSignalSource
    {
        get => _lookSignals ??= new LookSignals(_systemMonitorService, _nowPlayingService, _audioService, status: _statusBoard);
        set => _lookSignals = value;
    }

    internal bool IsLookTimerRunning => _lookTimer?.IsEnabled == true;

    /// <summary>The demand of all keys on the current view (what the monitor must sample, whether the timer has to run).</summary>
    private LookDemandResult CollectLookDemand()
    {
        var demand = LookDemandResult.None;
        foreach (var key in Keys)
            demand = demand.Merge(LookDemand.For(key.LookRules, key.CustomTitle, key.CustomSubtitle, key.ActiveCustomTitle, key.ActiveCustomSubtitle));
        return demand;
    }

    /// <summary>Starts the evaluator only when some key on the view has rules or tokens; stops it when none has (then it costs nothing).</summary>
    private void UpdateLookTimer(LookDemandResult demand)
    {
        if (_disposed) return;

        if (!demand.Any)
        {
            _lookTimer?.Stop();
            return;
        }

        _lookTimer ??= CreateLookTimer();
        if (!_lookTimer.IsEnabled) _lookTimer.Start();
    }

    private DispatcherTimer CreateLookTimer()
    {
        var timer = new DispatcherTimer { Interval = LookInterval };
        timer.Tick += (_, _) => EvaluateLooks();
        return timer;
    }

    /// <summary>One pass over the keys of the view: re-evaluates the rules and redraws the keys whose look changed.</summary>
    internal void EvaluateLooks()
    {
        if (_disposed || _deviceService.IsStandby) return;

        var signals = LookSignalSource;
        foreach (var key in Keys)
        {
            if (RefreshLook(key, signals)) RenderKeyVisual(key, sendToHardware: true);
        }
    }

    /// <summary>Evaluates the rules and the tokens of one key. <c>true</c> = its look changed and the picture has to be redrawn.</summary>
    internal bool RefreshLook(KeyItemViewModel key, ILookSignals signals)
    {
        var look = KeyLookEvaluator.Evaluate(key.LookRules, signals, key.LookState);

        string tokens = "";
        if (HasLabelTokens(key))
        {
            tokens = string.Join('\u0001',
                LabelTokens.Substitute(key.CustomTitle ?? "", signals),
                LabelTokens.Substitute(key.CustomSubtitle ?? "", signals),
                LabelTokens.Substitute(key.ActiveCustomTitle ?? "", signals),
                LabelTokens.Substitute(key.ActiveCustomSubtitle ?? "", signals));
        }

        string signature = look.Signature.Length == 0 && tokens.Length == 0 ? "" : look.Signature + "#" + tokens;

        key.AppliedLook = look;   // always fresh: a redraw for another reason (press, edit) must not use an old look
        if (signature == key.LookRenderSignature) return false;

        key.LookRenderSignature = signature;
        return true;
    }

    private static bool HasLabelTokens(KeyItemViewModel key) =>
        LabelTokens.HasTokens(key.CustomTitle) || LabelTokens.HasTokens(key.CustomSubtitle) ||
        LabelTokens.HasTokens(key.ActiveCustomTitle) || LabelTokens.HasTokens(key.ActiveCustomSubtitle);

    /// <summary>The label with its <c>{cpu}</c>-style tokens filled (a label without tokens is returned as it is).</summary>
    private string? FillTokens(string? text) =>
        LabelTokens.HasTokens(text) ? LabelTokens.Substitute(text!, LookSignalSource) : text;

    private void DisposeLooks()
    {
        _lookTimer?.Stop();
        _lookTimer = null;
    }
}
