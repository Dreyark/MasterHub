using Avalonia;
using Avalonia.Headless;
using AppType = global::MasterHub.Avalonia.App;
using MasterHub.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace MasterHub.UiTests;

/// <summary>
/// Uruchamia prawdziwy App (te same style/motyw co w aplikacji) w trybie headless z renderowaniem Skia,
/// bez okien i bez usług sprzętowych (App pomija je poza klasycznym desktopem).
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<AppType>()
        .UseSkia()
        .WithInterFont()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
