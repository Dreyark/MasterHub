using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

namespace MasterHub.UiTests;

/// <summary>Renderuje kontrolkę do PNG (katalog z zmiennej MH_SHOTS lub %TEMP%\mh_shots).</summary>
public static class Shot
{
    public static string Dir => Environment.GetEnvironmentVariable("MH_SHOTS")
                                ?? Path.Combine(Path.GetTempPath(), "mh_shots");

    public static string Render(Control content, string name, double width = 900, double height = 600,
        Action<Window>? beforeCapture = null)
    {
        Directory.CreateDirectory(Dir);
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            Background = (global::Avalonia.Media.IBrush?)Application.Current!.FindResource("MainBgBrush")
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        beforeCapture?.Invoke(window);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        string path = Path.Combine(Dir, name + ".png");
#pragma warning disable CS0618
        frame?.Save(path);
#pragma warning restore CS0618
        window.Close();
        return path;
    }
}
