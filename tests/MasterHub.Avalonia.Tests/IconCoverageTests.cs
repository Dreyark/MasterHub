using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using MasterHub.Avalonia.Views.Controls;
using MasterHub.Core.Actions;

namespace MasterHub.UiTests;

/// <summary>Każda akcja decka i kategoria musi dawać istniejącą ikonę wektorową (bez emoji i bez pustych kafelków).</summary>
public class IconCoverageTests
{
    private static bool Exists(string key)
        => Application.Current!.TryGetResource("Icon" + key, Application.Current.ActualThemeVariant, out var r) && r is Geometry;

    [AvaloniaFact]
    public void EveryDeckAction_HasExistingIcon()
    {
        foreach (var a in Enum.GetValues<DeckActionType>().Distinct())
        {
            string key = IconConverters.ForAction(a);
            Assert.True(Exists(key), $"{a} -> brak ikony 'Icon{key}' w Styles/Icons.axaml");
        }
    }

    [AvaloniaFact]
    public void EveryCategory_HasExistingIcon()
    {
        foreach (var cat in new[] { "Cat_System", "Cat_Audio", "Cat_MainAudio", "Cat_Media", "Cat_Spotify", "Cat_Vlc", "Cat_Voicemod", "Cat_Monitor",
                     "Cat_OBS", "Cat_Twitch", "Cat_Kick", "Cat_JetKvm", "Cat_Streaming", "Cat_Discord", "Cat_Teams", "Cat_Macros", "Cat_Razer",
                     "Cat_Wooting", "Cat_Nav", "Cat_Applications", "Cat_SmartHome", "Unknown" })
            Assert.True(Exists(IconConverters.ForCategoryKey(cat)), cat);
    }
}
