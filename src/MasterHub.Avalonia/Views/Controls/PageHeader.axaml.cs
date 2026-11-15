using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MasterHub.Avalonia.Views.Controls;

/// <summary>
/// Nagłówek strony używany przez wszystkie strony aplikacji (ten sam układ, rozmiar tytułu i odstępy).
/// Title/Subtitle przyjmują rozszerzenie lokalizacji (loc:Loc); Actions to dowolna zawartość po prawej (np. przyciski).
/// </summary>
public partial class PageHeader : UserControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Title));

    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<PageHeader, string?>(nameof(Subtitle));

    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<PageHeader, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<PageHeader, object?>(nameof(Actions));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    public PageHeader()
    {
        InitializeComponent();
        Sync();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TitleProperty || change.Property == SubtitleProperty ||
            change.Property == IconProperty || change.Property == ActionsProperty)
            Sync();
    }

    private void Sync()
    {
        if (TitlePart == null) return;   // przed InitializeComponent
        TitlePart.Text = Title;
        SubtitlePart.Text = Subtitle;
        SubtitlePart.IsVisible = !string.IsNullOrEmpty(Subtitle);
        IconPart.Data = Icon;
        IconPart.IsVisible = Icon != null;
        ToolTip.SetTip(SubtitlePart, Subtitle);
        ActionsPart.Content = Actions;
        ActionsPart.IsVisible = Actions != null;
    }
}
