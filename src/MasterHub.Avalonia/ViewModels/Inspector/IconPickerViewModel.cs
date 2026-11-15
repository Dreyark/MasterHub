using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MasterHub.Core.Rendering.Icons;
using SkiaSharp;

namespace MasterHub.Avalonia.ViewModels.Inspector;

/// <summary>One icon in the picker: its name and a small preview in the chosen colour.</summary>
public sealed class IconChoice
{
    public IconChoice(string name, string label, Bitmap? preview)
    {
        Name = name;
        Label = label;
        Preview = preview;
    }

    public string Name { get; }
    public string Label { get; }
    public Bitmap? Preview { get; }
}

/// <summary>
/// Searchable picker of the bundled icon library (T-21a): type a word (English names and tags of Lucide), pick a colour,
/// click an icon. The key then gets the icon as its picture (see <see cref="IconFiles"/>).
/// </summary>
public partial class IconPickerViewModel : ObservableObject
{
    public const int MaxResults = 60;
    private const int PreviewSize = 28;
    private const int MaxCachedPreviews = 800;

    /// <summary>Colours offered for the icon; white first because keys are dark.</summary>
    public static readonly IReadOnlyList<string> TintOptions = new[]
    {
        "#FFFFFF", "#A1A1AA", "#00FFCC", "#38BDF8", "#818CF8", "#C084FC",
        "#F472B6", "#F87171", "#FB923C", "#FACC15", "#4ADE80", "#000000"
    };

    private readonly IIconLibrary _library;
    // Previews are never disposed while the picker lives: a bitmap still on screen must not be freed.
    private readonly Dictionary<string, Bitmap?> _previews = new(StringComparer.Ordinal);

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private string _tintHex = "#FFFFFF";

    public IconPickerViewModel(IIconLibrary? library = null)
    {
        _library = library ?? IconLibrary.Shared;
        Refresh();
    }

    public ObservableCollection<IconChoice> Results { get; } = new();

    public int LibrarySize => _library.All.Count;

    public bool HasNoResults => Results.Count == 0;

    /// <summary>Raised with the icon name and colour when the user clicks an icon.</summary>
    public event Action<string, string>? Picked;

    partial void OnQueryChanged(string value) => Refresh();

    partial void OnTintHexChanged(string value) => Refresh();

    [RelayCommand]
    private void SelectTint(string? hex)
    {
        if (!string.IsNullOrWhiteSpace(hex)) TintHex = hex;
    }

    [RelayCommand]
    private void Pick(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || _library.Find(name) == null) return;
        Picked?.Invoke(name, TintHex);
    }

    private void Refresh()
    {
        Results.Clear();
        if (_previews.Count > MaxCachedPreviews) _previews.Clear();   // dropped, not disposed (see above)
        foreach (var entry in _library.Search(Query, MaxResults))
        {
            Results.Add(new IconChoice(entry.Name, entry.Label, Preview(entry.Name)));
        }
        OnPropertyChanged(nameof(HasNoResults));
    }

    private Bitmap? Preview(string name)
    {
        string key = name + "|" + TintHex;
        if (_previews.TryGetValue(key, out var cached)) return cached;
        Bitmap? bitmap = null;
        try
        {
            using SKBitmap? skia = _library.Render(name, TintHex, PreviewSize, paddingPercent: 4);
            if (skia != null)
            {
                using var image = SKImage.FromBitmap(skia);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                using var stream = new MemoryStream(data.ToArray());
                bitmap = new Bitmap(stream);
            }
        }
        catch
        {
            bitmap = null;
        }
        _previews[key] = bitmap;
        return bitmap;
    }
}
