using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Localization;
using MasterHub.Core.Profiles;
using MasterHub.Core.Profiles.Templates;

namespace MasterHub.Avalonia.ViewModels.Pages;

/// <summary>One key in the preview grid of a template.</summary>
public sealed record TemplateKeyPreview(string Title, IBrush Background);

/// <summary>A ready-made profile in the picker.</summary>
public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(ProfileTemplate template)
    {
        Template = template;
        string lang = (LocalizationService.Instance.CurrentLanguageCode ?? "en").Split('-')[0];
        Name = template.NameFor(lang);
        Description = template.DescriptionFor(lang);
        AppsText = template.Exes.Count == 0 ? "" : string.Join(", ", template.Exes);

        // preview of the 3 × 5 key panel, in key order
        Preview = Enumerable.Range(0, 15).Select(i =>
        {
            var key = template.Keys.FirstOrDefault(k => k.Index == i);
            IBrush brush = new SolidColorBrush(key != null && Color.TryParse(key.Background, out var c) ? c : Color.Parse("#14152A"));
            return new TemplateKeyPreview(key?.Title ?? "", brush);
        }).ToList();
    }

    public ProfileTemplate Template { get; }
    public string Name { get; }
    public string Description { get; }
    public string AppsText { get; }
    public bool HasApps => AppsText.Length > 0;
    public IReadOnlyList<TemplateKeyPreview> Preview { get; }
}

/// <summary>"New profile → From a ready-made profile": pick a template, see what it contains, then add it.</summary>
public sealed partial class TemplatePickerViewModel : ObservableObject
{
    private readonly IProfileTemplateService? _service;

    public ObservableCollection<TemplateItemViewModel> Items { get; } = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private TemplateItemViewModel? _selected;

    /// <summary>Also link the template's apps as Smart Profile rules (off by default: nothing is linked behind the user's back).</summary>
    [ObservableProperty] private bool _linkApps;

    /// <summary>Raised with the new profile after it was created.</summary>
    public event Action<ProfileModel>? Added;

    /// <summary>Raised with the profile the template describes (not saved) when the user wants to try it on the deck first.</summary>
    public event Action<ProfileModel>? PreviewRequested;

    public TemplatePickerViewModel(IProfileTemplateService? service) => _service = service;

    public bool IsAvailable => _service != null;

    [RelayCommand]
    private void Open()
    {
        if (_service == null) return;
        Items.Clear();
        foreach (var t in _service.GetAll()) Items.Add(new TemplateItemViewModel(t));
        Selected = Items.FirstOrDefault();
        LinkApps = false;
        IsOpen = true;
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    /// <summary>Shows the template on the deck for 20 seconds without saving anything; "Keep" in the banner adds it.</summary>
    [RelayCommand]
    private void Preview()
    {
        if (_service == null || Selected == null) return;

        var profile = _service.Build(Selected.Template, Selected.Name);
        IsOpen = false;
        PreviewRequested?.Invoke(profile);
    }

    [RelayCommand]
    private void Add()
    {
        if (_service == null || Selected == null) return;

        var profile = _service.Instantiate(Selected.Template, Selected.Name, LinkApps);
        IsOpen = false;
        Added?.Invoke(profile);
    }
}
