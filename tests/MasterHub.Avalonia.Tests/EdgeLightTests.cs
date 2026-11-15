using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels;
using MasterHub.Avalonia.Views.Inspector;
using MasterHub.Core.Devices;

namespace MasterHub.UiTests;

/// <summary>T-18 in the Studio: the base's edge effect per profile and the status-light reactions.</summary>
public class EdgeLightTests
{
    [AvaloniaFact]
    public void EdgeEffect_RaisesTheCallback_OnlyOnChange_AndLoadingIsSilent()
    {
        var vm = new BaseStationItemViewModel();
        int calls = 0;
        vm.EffectChangedCallback = _ => calls++;

        vm.EdgeEffect = LedEffect.Breathing;
        vm.EdgeEffect = LedEffect.Breathing;   // same value: nothing
        vm.EdgeSpeed = 80;
        Assert.Equal(2, calls);

        vm.LoadEdge(LedEffect.Wave, 20);        // a profile was applied: no hardware write storm
        Assert.Equal(2, calls);
        Assert.Equal(LedEffect.Wave, vm.EdgeEffect);
        Assert.Equal((byte)20, vm.EdgeSpeed);
    }

    [AvaloniaFact]
    public void Speed_IsOnlyShownForAnimatedEffects_AndIsClamped()
    {
        var vm = new BaseStationItemViewModel();
        Assert.False(vm.ShowsEdgeSpeed);                 // static

        vm.EdgeEffect = LedEffect.Rainbow;
        Assert.True(vm.ShowsEdgeSpeed);

        vm.EdgeEffect = LedEffect.Disable;
        Assert.False(vm.ShowsEdgeSpeed);

        vm.LoadEdge(LedEffect.Wave, 500);
        Assert.Equal((byte)100, vm.EdgeSpeed);
    }

    [AvaloniaFact]
    public void EffectOptions_CoverEveryEffect_WithNames()
    {
        var options = BaseStationItemViewModel.EffectOptions;
        Assert.Equal(Enum.GetValues<LedEffect>().Length, options.Count);
        Assert.All(options, o => Assert.False(string.IsNullOrWhiteSpace(o.Name)));

        var vm = new BaseStationItemViewModel();
        vm.SelectedEdgeEffect = options.First(o => o.Effect == LedEffect.Strobe);
        Assert.Equal(LedEffect.Strobe, vm.EdgeEffect);
    }

    [AvaloniaFact]
    public void StatusLightReactions_NotifyAndLoadSilently()
    {
        var vm = new BaseStationItemViewModel();
        int calls = 0;
        vm.StatusLightsChangedCallback = _ => calls++;

        vm.StatusLightsEnabled = true;
        vm.StatusMicMuted = false;
        Assert.Equal(2, calls);

        vm.LoadStatusLights(new StatusLightSettings { Enabled = false, MicMuted = true, ObsLive = false, DiscordMuted = true });
        Assert.Equal(2, calls);
        Assert.False(vm.StatusLightsEnabled);
        Assert.True(vm.StatusMicMuted);
        Assert.False(vm.StatusObsLive);
    }

    [AvaloniaFact]
    public void Inspector_Section_Renders()
    {
        var vm = new BaseStationItemViewModel();
        vm.EdgeEffect = LedEffect.Breathing;
        vm.StatusLightsEnabled = true;

        Assert.True(File.Exists(Shot.Render(new BaseEdgeEffectView { DataContext = vm }, "inspector_base_edge", 340, 600)));
        Assert.True(File.Exists(Shot.Render(new BaseStationInspectorView { DataContext = vm }, "inspector_base_full", 340, 1100)));
    }
}
