using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using MasterHub.Avalonia.Views.Canvas;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Avalonia.Views.Navigation;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Avalonia.Views.Window;

namespace MasterHub.UiTests;

/// <summary>
/// Każdy widok aplikacji musi dać się utworzyć i wyrenderować (bez DataContextu = sam szkielet/styl).
/// Łapie błędy XAML wykrywane dopiero w runtime: brakujące zasoby/assety, błędne selektory, wyjątki w konstruktorach.
/// </summary>
public class ViewSmokeTests
{
    public static IEnumerable<object[]> Views() => new[]
    {
        new object[] { typeof(TitleBarView) },
        new object[] { typeof(NavigationSidebarView) },
        new object[] { typeof(ModuleCanvasView) },
        new object[] { typeof(KeysModuleView) },
        new object[] { typeof(SlidersModuleView) },
        new object[] { typeof(RollersModuleView) },
        new object[] { typeof(KnobsModuleView) },
        new object[] { typeof(DialModuleView) },
        new object[] { typeof(BaseStationView) },
        new object[] { typeof(InspectorPanelView) },
        new object[] { typeof(KeyInspectorView) },
        new object[] { typeof(KeyActionSettingsView) },
        new object[] { typeof(KeyDisplaySettingsView) },
        new object[] { typeof(KeyTitleSettingsView) },
        new object[] { typeof(KeySubtitleSettingsView) },
        new object[] { typeof(KeyTypographySettingsView) },
        new object[] { typeof(KeyMediaTransformView) },
        new object[] { typeof(KeyPanoramicSettingsSectionView) },
        new object[] { typeof(KeyStreamDeckSettingsSectionView) },
        new object[] { typeof(KeyVoicemodSettingsSectionView) },
        new object[] { typeof(KeyHomeAssistantSettingsSectionView) },
        new object[] { typeof(KeyNavigationSettingsSectionView) },
        new object[] { typeof(SliderInspectorView) },
        new object[] { typeof(RollerInspectorView) },
        new object[] { typeof(KnobInspectorView) },
        new object[] { typeof(DialInspectorView) },
        new object[] { typeof(BaseStationInspectorView) },
        new object[] { typeof(ActionLibraryView) },
        new object[] { typeof(DeviceHeaderCardView) },
        new object[] { typeof(SettingsPageView) },
        new object[] { typeof(ProfilesPageView) },
        new object[] { typeof(ActionStorePageView) },
        new object[] { typeof(PluginCatalogTabView) },
        new object[] { typeof(LogsPageView) },
        new object[] { typeof(GeneralSettingsCardView) },
        new object[] { typeof(DiscordSettingsCardView) },
        new object[] { typeof(HomeAssistantSettingsCardView) },
        new object[] { typeof(JetKvmSettingsCardView) },
        new object[] { typeof(KickSettingsCardView) },
        new object[] { typeof(ObsSettingsCardView) },
        new object[] { typeof(SpotifySettingsCardView) },
        new object[] { typeof(TwitchSettingsCardView) },
        new object[] { typeof(VlcSettingsCardView) },
        new object[] { typeof(VoicemodSettingsCardView) },
        new object[] { typeof(MacrosPageView) },
        new object[] { typeof(MacroEditorView) },
        new object[] { typeof(MacroStepsView) },
        new object[] { typeof(MacroBindingsView) },
        new object[] { typeof(KeyMacroSettingsSectionView) },
        new object[] { typeof(MasterHub.Avalonia.Views.Controls.PageHeader) },
        new object[] { typeof(BackupsCardView) },
        new object[] { typeof(KeyGestureTabsView) },
        new object[] { typeof(KeyAudioSwitchSettingsView) },
        new object[] { typeof(KeyLiveValueSettingsView) },
        new object[] { typeof(KeyActionResultView) },
        new object[] { typeof(KeyHotkeyModeView) },
        new object[] { typeof(TemplatePickerPanelView) },
        new object[] { typeof(BaseEdgeEffectView) },
        new object[] { typeof(PowerConfirmSettingsView) },
        new object[] { typeof(MasterHub.Avalonia.Views.Controls.ConfirmOverlayView) },
        new object[] { typeof(MasterHub.Avalonia.Views.Controls.QuarantineBannerView) },
    };

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public void View_CreatesAndRenders(Type viewType)
    {
        var view = (Control)Activator.CreateInstance(viewType)!;
        string path = Shot.Render(view, "view_" + viewType.Name, 520, 700);
        Assert.True(File.Exists(path));
    }

    [AvaloniaFact]
    public void PluginSettingsWindow_CreatesAndRenders()
    {
        var window = new PluginSettingsWindow();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Directory.CreateDirectory(Shot.Dir);
#pragma warning disable CS0618
        frame?.Save(Path.Combine(Shot.Dir, "window_PluginSettings.png"));
#pragma warning restore CS0618
        window.Close();
    }
}
