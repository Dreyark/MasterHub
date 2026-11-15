using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MasterHub.Core.Dialogs;

/// <summary>What the user is asked. Buttons carry the verb ("Delete profile"), never a bare "OK".</summary>
/// <param name="Title">Heading, e.g. "Delete profile?".</param>
/// <param name="Message">Explanation of what happens.</param>
/// <param name="ConfirmLabel">Text of the confirming button (the verb).</param>
/// <param name="CancelLabel">Text of the cancelling button.</param>
/// <param name="Destructive">Paints the confirm button as dangerous.</param>
public sealed record ConfirmRequest(string Title, string Message, string ConfirmLabel, string CancelLabel, bool Destructive = true);

/// <summary>
/// Awaitable in-app confirmation (an overlay hosted in the main window, not a system message box). Requests are shown
/// one at a time in the order they were made; <c>false</c> is returned for cancel, timeout and cancellation.
/// </summary>
public interface IConfirmDialogService
{
    /// <summary>The request currently shown, or <c>null</c>.</summary>
    ConfirmRequest? Current { get; }

    /// <summary>Raised (on an arbitrary thread) whenever <see cref="Current"/> changes.</summary>
    event Action? CurrentChanged;

    /// <summary>Asks the user. Resolves to <c>true</c> only when the confirm button is pressed.</summary>
    Task<bool> ConfirmAsync(ConfirmRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default);

    /// <summary>Called by the overlay: the confirm button was pressed.</summary>
    void Accept();

    /// <summary>Called by the overlay: cancel / Esc / click outside.</summary>
    void Cancel();
}

/// <summary>Default implementation (UI-independent, so it is unit-testable).</summary>
public sealed class ConfirmDialogService : IConfirmDialogService
{
    private sealed class Pending
    {
        public required ConfirmRequest Request { get; init; }
        public required TaskCompletionSource<bool> Completion { get; init; }
        public required TimeSpan? Timeout { get; init; }
        public CancellationTokenSource? Timer { get; set; }
        public CancellationTokenSource? Linked { get; set; }
    }

    private readonly object _lock = new();
    private readonly Queue<Pending> _queue = new();
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private Pending? _current;

    public ConfirmDialogService(Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _delay = delay ?? Task.Delay;
    }

    public event Action? CurrentChanged;

    public ConfirmRequest? Current
    {
        get { lock (_lock) { return _current?.Request; } }
    }

    public Task<bool> ConfirmAsync(ConfirmRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested) return Task.FromResult(false);

        var pending = new Pending
        {
            Request = request,
            Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
            Timeout = timeout
        };

        if (cancellationToken.CanBeCanceled)
        {
            pending.Linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            pending.Linked.Token.Register(() => Resolve(pending, false));
        }

        bool shown;
        lock (_lock)
        {
            _queue.Enqueue(pending);
            shown = TryShowNext();
        }

        if (shown) CurrentChanged?.Invoke();
        return pending.Completion.Task;
    }

    public void Accept() => ResolveCurrent(true);

    public void Cancel() => ResolveCurrent(false);

    private void ResolveCurrent(bool result)
    {
        Pending? pending;
        lock (_lock) { pending = _current; }
        if (pending != null) Resolve(pending, result);
    }

    private void Resolve(Pending pending, bool result)
    {
        bool changed = false;
        lock (_lock)
        {
            if (pending.Completion.Task.IsCompleted) return;

            pending.Timer?.Cancel();
            pending.Linked?.Dispose();
            pending.Completion.TrySetResult(result);

            if (ReferenceEquals(_current, pending))
            {
                _current = null;
                TryShowNext();
                changed = true;
            }
            // A request still waiting in the queue is dropped when it becomes current (see TryShowNext).
        }

        if (changed) CurrentChanged?.Invoke();
    }

    /// <summary>Makes the next live request current. Must be called with <c>_lock</c> held.</summary>
    private bool TryShowNext()
    {
        if (_current != null) return false;

        while (_queue.Count > 0)
        {
            var next = _queue.Dequeue();
            if (next.Completion.Task.IsCompleted) continue;

            _current = next;
            if (next.Timeout is { } timeout)
            {
                next.Timer = new CancellationTokenSource();
                var token = next.Timer.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _delay(timeout, token).ConfigureAwait(false);
                        if (!token.IsCancellationRequested) Resolve(next, false);
                    }
                    catch (OperationCanceledException) { /* answered in time */ }
                });
            }
            return true;
        }

        return false;
    }
}

/// <summary>
/// Confirms everything immediately. Used only where no overlay exists (headless tests, tools); the application
/// registers <see cref="ConfirmDialogService"/> so users are always asked.
/// </summary>
public sealed class AutoConfirmDialogService : IConfirmDialogService
{
    public ConfirmRequest? Current => null;

    public event Action? CurrentChanged
    {
        add { }
        remove { }
    }

    public Task<bool> ConfirmAsync(ConfirmRequest request, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(true);

    public void Accept() { }

    public void Cancel() { }
}
