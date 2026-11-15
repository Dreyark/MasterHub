using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Notices;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>One banner as the window shows it.</summary>
public sealed class NoticeItem
{
    public NoticeItem(Notice notice)
    {
        Id = notice.Id;
        Text = notice.Text;
        IsError = notice.Level == NoticeLevel.Error;
        IsWarning = notice.Level == NoticeLevel.Warning;
        IsInfo = notice.Level == NoticeLevel.Info;
    }

    public long Id { get; }
    public string Text { get; }
    public bool IsError { get; }
    public bool IsWarning { get; }
    public bool IsInfo { get; }
}

/// <summary>
/// The banners at the top of the window (T-34 #8). Mirrors <see cref="INoticeCenter"/> on the UI thread and asks it about once
/// a second to drop banners whose time is up.
/// </summary>
public partial class NoticeBannerViewModel : ObservableObject, IDisposable
{
    private readonly INoticeCenter _center;
    private DispatcherTimer? _timer;

    public NoticeBannerViewModel(INoticeCenter? center = null, bool startTimer = true)
    {
        _center = center ?? NoticeCenter.Shared;
        _center.Changed += OnChanged;
        Sync();
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => _center.Prune();
            _timer.Start();
        }
    }

    public ObservableCollection<NoticeItem> Items { get; } = new();

    public bool HasItems => Items.Count > 0;

    [RelayCommand]
    private void Dismiss(long id) => _center.Dismiss(id);

    [RelayCommand]
    private void DismissAll() => _center.DismissAll();

    private void OnChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Sync();
        else Dispatcher.UIThread.Post(Sync);
    }

    internal void Sync()
    {
        Items.Clear();
        foreach (var notice in _center.Current) Items.Add(new NoticeItem(notice));
        OnPropertyChanged(nameof(HasItems));
    }

    public void Dispose()
    {
        _center.Changed -= OnChanged;
        _timer?.Stop();
        _timer = null;
    }
}
