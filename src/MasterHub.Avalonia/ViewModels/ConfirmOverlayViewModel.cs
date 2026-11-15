using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Dialogs;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>
/// Presentation model of the in-app confirmation overlay hosted in <c>MainWindow</c>: mirrors
/// <see cref="IConfirmDialogService.Current"/> and forwards the two buttons.
/// </summary>
public sealed partial class ConfirmOverlayViewModel : ObservableObject
{
    private readonly IConfirmDialogService _service;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private string _confirmLabel = "";
    [ObservableProperty] private string _cancelLabel = "";
    [ObservableProperty] private bool _isDestructive = true;

    public ConfirmOverlayViewModel(IConfirmDialogService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _service.CurrentChanged += OnCurrentChanged;
        Refresh();
    }

    private void OnCurrentChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Refresh();
        else Dispatcher.UIThread.Post(Refresh);
    }

    private void Refresh()
    {
        var current = _service.Current;
        if (current == null)
        {
            IsVisible = false;
            return;
        }

        Title = current.Title;
        Message = current.Message;
        ConfirmLabel = current.ConfirmLabel;
        CancelLabel = current.CancelLabel;
        IsDestructive = current.Destructive;
        IsVisible = true;
    }

    [RelayCommand]
    private void Accept() => _service.Accept();

    [RelayCommand]
    private void Cancel() => _service.Cancel();
}
