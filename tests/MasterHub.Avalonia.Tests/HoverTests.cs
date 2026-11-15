using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using SkiaSharp;

namespace MasterHub.UiTests;

#pragma warning disable CS0618
public class HoverTests
{
    /// <summary>
    /// Dla każdej klasy przycisku renderuje stan normalny i :pointerover obok siebie (lewa kolumna = normal, prawa = hover)
    /// i składa wszystko w jeden obraz.
    /// </summary>
    [AvaloniaFact]
    public void Buttons_NormalVsHover()
    {
        string[] classes = { "actionBtn", "accentBtn", "pillBtn", "iconBtn", "navItem", "ghostBtn", "" };
        var rows = new List<(SKBitmap normal, SKBitmap hover)>();
        foreach (var cls in classes)
        {
            (SKBitmap n, SKBitmap hv) = (Capture(cls, hover: false), Capture(cls, hover: true));
            rows.Add((n, hv));
        }

        int w = rows[0].normal.Width * 2 + 10, h = rows.Sum(r => r.normal.Height) + 4 * rows.Count;
        using var composite = new SKBitmap(w, h);
        using (var canvas = new SKCanvas(composite))
        {
            canvas.Clear(new SKColor(0x18, 0x18, 0x24));
            int y = 0;
            foreach (var (n, hv) in rows)
            {
                canvas.DrawBitmap(n, 0f, y, (SKPaint?)null);
                canvas.DrawBitmap(hv, n.Width + 10f, y, (SKPaint?)null);
                y += n.Height + 4;
            }
        }
        Directory.CreateDirectory(Shot.Dir);
        using var img = SKImage.FromBitmap(composite);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(Shot.Dir, "buttons_normal_vs_hover.png"), data.ToArray());
    }

    private static SKBitmap Capture(string cls, bool hover)
    {
        var button = new Button
        {
            Content = string.IsNullOrEmpty(cls) ? "(plain) Button" : cls,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12)
        };
        if (!string.IsNullOrEmpty(cls)) button.Classes.Add(cls);

        var window = new Window
        {
            Width = 260,
            Height = 70,
            Content = button,
            Background = (global::Avalonia.Media.IBrush?)Application.Current!.FindResource("MainBgBrush")
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        if (hover)
        {
            var p = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(p);
            Dispatcher.UIThread.RunJobs();
        }
        var frame = window.CaptureRenderedFrame()!;
        using var ms = new MemoryStream();
#pragma warning disable CS0618
        frame.Save(ms);
#pragma warning restore CS0618
        ms.Position = 0;
        var bmp = SKBitmap.Decode(ms);
        window.Close();
        return bmp;
    }
}
