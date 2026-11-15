namespace MasterHub.Core.Macros;

/// <summary>Abstrakcja wstrzykiwania wejścia (klawiatura / mysz) - w testach zastępowana atrapą.</summary>
public interface IInputSimulator
{
    void KeyDown(int vk, bool extended);
    void KeyUp(int vk, bool extended);
    void MouseDown(MacroMouseButton button);
    void MouseUp(MacroMouseButton button);
    void Wheel(int delta, bool horizontal);
    void TypeText(string text);
}
