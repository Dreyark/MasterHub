using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Actions;
using MasterHub.Core.LiveValues;

namespace MasterHub.Avalonia.ViewModels;

/// <summary>An entry of the "source" list of a live-value key.</summary>
public sealed record LiveSourceOption(LiveValueSourceKind Kind, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Edits the <see cref="LiveValueSettings"/> of one key (the inspector section binds to it).</summary>
public sealed partial class LiveValueEditor : ObservableObject
{
    private readonly KeyItemViewModel _key;

    public LiveValueEditor(KeyItemViewModel key) => _key = key;

    private LiveValueSettings Model => _key.LiveSettings;

    public IReadOnlyList<LiveSourceOption> Sources => KeyItemViewModel.LiveSourceOptions;

    public LiveSourceOption SelectedSource
    {
        get => KeyItemViewModel.LiveSourceOptions.First(o => o.Kind == Model.Kind);
        set { Model.Kind = value.Kind; Touch(nameof(SelectedSource), nameof(ShowsUrl), nameof(ShowsPath), nameof(ShowsCity), nameof(ShowsEntity), nameof(ShowsCommand), nameof(ShowsToken), nameof(NeedsApproval)); }
    }

    public bool ShowsUrl => Model.Kind is LiveValueSourceKind.WebAddress or LiveValueSourceKind.CalendarNext;
    public bool ShowsPath => Model.Kind == LiveValueSourceKind.WebAddress;
    public bool ShowsCity => Model.Kind == LiveValueSourceKind.Weather;
    public bool ShowsEntity => Model.Kind == LiveValueSourceKind.HomeAssistant;
    public bool ShowsCommand => Model.Kind == LiveValueSourceKind.Command;
    public bool ShowsToken => ShowsUrl;

    public string Url { get => Model.Url; set { Model.Url = value ?? ""; Touch(nameof(Url)); } }
    public string JsonPath { get => Model.JsonPath; set { Model.JsonPath = value ?? ""; Touch(nameof(JsonPath)); } }
    public string City { get => Model.City; set { Model.City = value ?? ""; Touch(nameof(City)); } }
    public string EntityId { get => Model.EntityId; set { Model.EntityId = value ?? ""; Touch(nameof(EntityId)); } }
    public string Command { get => Model.Command; set { Model.Command = value ?? ""; Touch(nameof(Command), nameof(NeedsApproval)); } }
    public int RefreshSeconds { get => Model.RefreshSeconds; set { Model.RefreshSeconds = Math.Max(5, value); Touch(nameof(RefreshSeconds)); } }
    public string Before { get => Model.Before; set { Model.Before = value ?? ""; Touch(nameof(Before)); } }
    public string After { get => Model.After; set { Model.After = value ?? ""; Touch(nameof(After)); } }
    public int Decimals { get => Model.Decimals; set { Model.Decimals = Math.Clamp(value, -1, 10); Touch(nameof(Decimals)); } }

    public string WarnAtText { get => Show(Model.WarnAt); set { Model.WarnAt = Parse(value); Touch(nameof(WarnAtText)); } }
    public string AlertAtText { get => Show(Model.AlertAt); set { Model.AlertAt = Parse(value); Touch(nameof(AlertAtText)); } }
    public string MaximumText { get => Show(Model.Maximum); set { Model.Maximum = Parse(value); Touch(nameof(MaximumText)); } }

    public bool WorseWhenHigher
    {
        get => Model.Worse == WorseWhen.Higher;
        set { Model.Worse = value ? WorseWhen.Higher : WorseWhen.Lower; Touch(nameof(WorseWhenHigher), nameof(WorseWhenLower)); }
    }

    public bool WorseWhenLower => !WorseWhenHigher;

    /// <summary>A Bearer token typed here goes to the DPAPI token store; the profile keeps only its name. Always empty on load.</summary>
    [ObservableProperty]
    private string _tokenInput = "";

    public bool HasToken => !string.IsNullOrWhiteSpace(Model.TokenName);

    /// <summary>A command key that has not been approved yet (it will not run until the user allows exactly this line).</summary>
    public bool NeedsApproval => Model.Kind == LiveValueSourceKind.Command && Model.IsConfigured && !_key.IsLiveCommandApproved();

    partial void OnTokenInputChanged(string value)
    {
        if (string.IsNullOrEmpty(value)) return;
        _key.StoreLiveToken(value);
        Touch(nameof(HasToken));
    }

    [RelayCommand]
    private void ClearToken()
    {
        _key.StoreLiveToken("");
        TokenInput = "";
        Touch(nameof(HasToken));
    }

    [RelayCommand]
    private void ApproveCommand()
    {
        _key.ApproveLiveCommand();
        OnPropertyChanged(nameof(NeedsApproval));
    }

    private void Touch(params string[] names)
    {
        foreach (string n in names) OnPropertyChanged(n);
        _key.NotifyLiveSettingsChanged();
    }

    private static string Show(double? value) => value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";

    private static double? Parse(string? text) => LiveValueFormatter.TryNumber(text);
}

/// <summary>Live value (T-11): a key that shows a value from a web address, the weather, the next calendar event, Home Assistant or a command.</summary>
public partial class KeyItemViewModel
{
    private LiveValueEditor? _liveEditor;

    /// <summary>The key's live-value settings (always present; only saved for a <see cref="DeckActionType.LiveValue"/> key).</summary>
    public LiveValueSettings LiveSettings { get; private set; } = new();

    public LiveValueEditor LiveEditor => _liveEditor ??= new LiveValueEditor(this);

    public bool IsLiveValueAction => AssignedAction == DeckActionType.LiveValue;

    /// <summary>How serious the shown value is (not saved): colours the key.</summary>
    public LiveSeverity LiveSeverity { get; set; }

    /// <summary>The background the key is drawn with: the severity colour while a warning/alert holds, otherwise the chosen one.</summary>
    public string RenderBackgroundHex => LiveSeverity switch
    {
        LiveSeverity.Alert => "#B3261E",
        LiveSeverity.Warning => "#B45309",
        _ => TimerBackground ?? AppliedLook.Background ?? BackgroundColorHex
    };

    public static IReadOnlyList<LiveSourceOption> LiveSourceOptions => new LiveSourceOption[]
    {
        new(LiveValueSourceKind.None, MasterHub.Core.Localization.LocalizationService.Instance.GetString("Live_SourceNone", "— choose a source —")),
        new(LiveValueSourceKind.WebAddress, MasterHub.Core.Localization.LocalizationService.Instance.GetString("Live_SourceWeb", "Web address (text / JSON)")),
        new(LiveValueSourceKind.Weather, MasterHub.Core.Localization.LocalizationService.Instance.GetString("Live_SourceWeather", "Weather")),
        new(LiveValueSourceKind.CalendarNext, MasterHub.Core.Localization.LocalizationService.Instance.GetString("Live_SourceCalendar", "Next calendar event")),
        new(LiveValueSourceKind.HomeAssistant, MasterHub.Core.Localization.LocalizationService.Instance.GetString("Live_SourceHa", "Home Assistant entity")),
        new(LiveValueSourceKind.Command, MasterHub.Core.Localization.LocalizationService.Instance.GetString("Live_SourceCommand", "Command (first output line)"))
    };

    public void LoadLiveValue(LiveValueSettings? model)
    {
        LiveSettings = model ?? new LiveValueSettings();
        _liveEditor = null;
        OnPropertyChanged(nameof(LiveSettings));
        OnPropertyChanged(nameof(LiveEditor));
    }

    /// <summary>The settings to save, or <c>null</c> for a key that is not a live-value key.</summary>
    public LiveValueSettings? ToLiveModel() => IsLiveValueAction ? LiveSettings : null;

    internal void NotifyLiveSettingsChanged()
    {
        OnPropertyChanged(nameof(LiveSettings));
        ActionChangedCallback?.Invoke(this);
    }

    internal bool IsLiveCommandApproved() => GetService<ILiveValueService>()?.IsCommandApproved(LiveSettings.Command) ?? false;

    internal void ApproveLiveCommand()
    {
        GetService<ILiveValueService>()?.ApproveCommand(LiveSettings.Command);
        NotifyLiveSettingsChanged();
    }

    internal void StoreLiveToken(string token)
    {
        var store = GetService<LiveTokenStore>();
        if (store == null) return;

        if (string.IsNullOrEmpty(token))
        {
            if (!string.IsNullOrEmpty(LiveSettings.TokenName)) store.Set(LiveSettings.TokenName, "");
            LiveSettings.TokenName = "";
        }
        else
        {
            if (string.IsNullOrEmpty(LiveSettings.TokenName)) LiveSettings.TokenName = "live-" + Guid.NewGuid().ToString("N")[..12];
            store.Set(LiveSettings.TokenName, token);
        }
        NotifyLiveSettingsChanged();
    }

    private void ResetLiveValue()
    {
        LiveSettings = new LiveValueSettings();
        _liveEditor = null;
        LiveSeverity = LiveSeverity.Normal;
    }
}
