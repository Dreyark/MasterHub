using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MasterHub.Avalonia.ViewModels.Pages;

public class LogEntryItemViewModel : ObservableObject
{
    public DateTime Timestamp { get; }
    private readonly Func<string> _messageProvider;

    public string Text => $"[{Timestamp:HH:mm:ss.fff}] {_messageProvider()}";

    public LogEntryItemViewModel(DateTime timestamp, Func<string> messageProvider)
    {
        Timestamp = timestamp;
        _messageProvider = messageProvider ?? throw new ArgumentNullException(nameof(messageProvider));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Text));
    }

    public override string ToString() => Text;
}
