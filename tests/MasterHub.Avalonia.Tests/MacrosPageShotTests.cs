using Avalonia.Headless.XUnit;
using MasterHub.Avalonia.ViewModels.Pages;
using MasterHub.Avalonia.Views.Pages;
using MasterHub.Core.Macros;

namespace MasterHub.UiTests;

public class MacrosPageShotTests
{
    [AvaloniaFact]
    public void MacrosPage_WithSampleMacro_RendersEditorStepsAndBindings()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mh_macro_ui_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var service = new MacroService(new MacroStore(dir));   // bez Start() - żadne haki nie są uruchamiane
            var macro = new MacroDefinition
            {
                Name = "Szybki combo",
                Mode = MacroPlaybackMode.RepeatCount,
                RepeatCount = 5,
                Steps =
                {
                    MacroStep.KeyDown(0x11), MacroStep.KeyDown(0x43), MacroStep.Delay(40), MacroStep.KeyUp(0x43), MacroStep.KeyUp(0x11),
                    MacroStep.MouseDown(MacroMouseButton.Left), MacroStep.Delay(120), MacroStep.MouseUp(MacroMouseButton.Left),
                    MacroStep.Wheel(-120), MacroStep.TypeText("GG")
                }
            };
            service.SaveMacro(macro);
            service.SaveMacro(new MacroDefinition { Name = "Pusta pętla", Mode = MacroPlaybackMode.Toggle });
            service.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.MouseButton, Code = (int)MacroMouseButton.X1, MacroId = macro.Id });
            service.SaveBinding(new MacroBinding { TriggerKind = MacroTriggerKind.Key, Code = 0x7C, MacroId = macro.Id });

            var vm = new MacrosPageViewModel(service);
            string path = Shot.Render(new MacrosPageView { DataContext = vm }, "page_macros", 1200, 1000);

            Assert.True(File.Exists(path));
            Assert.Equal(2, vm.Items.Count);
            Assert.Equal(10, vm.Steps.Count);
            Assert.Equal(2, vm.Bindings.Count);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
