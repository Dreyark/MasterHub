namespace MasterHub.Core.Actions;

/// <summary>
/// Pojedynczy krok w sekwencji Multi-Action (Etap 1).
/// </summary>
public class MultiActionStepModel
{
    public DeckActionType Action { get; set; } = DeckActionType.None;
    public string Parameter { get; set; } = "";
    public int DelayMs { get; set; } = 0; // Opcjonalne opóźnienie po wykonaniu akcji

    public override string ToString() => DelayMs > 0
        ? $"{Action}('{Parameter}') [Delay: {DelayMs}ms]"
        : $"{Action}('{Parameter}')";
}
