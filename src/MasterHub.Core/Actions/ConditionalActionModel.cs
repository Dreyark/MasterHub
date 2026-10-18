using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MasterHub.Core.Actions;

public enum ConditionType
{
    ProcessRunning = 0,       // Czy proces jest uruchomiony (np. obs64, spotify, discord)
    ProcessNotRunning = 1,    // Czy proces NIE jest uruchomiony
    FileExists = 2,           // Czy plik lub folder istnieje na dysku
    ToggleStateActive = 3,    // Czy klawisz jest w stanie aktywnym (Stan 2)
    WindowForeground = 4      // Czy okno procesu/tytułu znajduje się na pierwszym planie
}

/// <summary>
/// Model reprezentujący regułę akcji warunkowej (IF / THEN / ELSE) dla klawiszy MasterHub (Etap 8.2).
/// </summary>
public class ConditionalActionModel
{
    public ConditionType Condition { get; set; } = ConditionType.ProcessRunning;
    public string Target { get; set; } = "";
    public DeckActionType IfTrueAction { get; set; } = DeckActionType.None;
    public string IfTrueParameter { get; set; } = "";
    public DeckActionType IfFalseAction { get; set; } = DeckActionType.None;
    public string IfFalseParameter { get; set; } = "";

    public bool Evaluate(bool currentToggleState = false)
    {
        try
        {
            return Condition switch
            {
                ConditionType.ProcessRunning => IsProcessRunning(Target),
                ConditionType.ProcessNotRunning => !IsProcessRunning(Target),
                ConditionType.FileExists => !string.IsNullOrWhiteSpace(Target) && (File.Exists(Target) || Directory.Exists(Target)),
                ConditionType.ToggleStateActive => currentToggleState,
                ConditionType.WindowForeground => IsWindowForeground(Target),
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    private static bool IsProcessRunning(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string procName = Path.GetFileNameWithoutExtension(name).Trim();
        return Process.GetProcessesByName(procName).Length > 0;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    private static bool IsWindowForeground(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return false;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return false;

        try
        {
            var proc = Process.GetProcessById((int)pid);
            string cleanTarget = Path.GetFileNameWithoutExtension(target).Trim();
            return proc.ProcessName.Equals(cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                   proc.MainWindowTitle.Contains(target, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string ToParameterString()
    {
        return JsonSerializer.Serialize(this);
    }

    public static ConditionalActionModel Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new ConditionalActionModel();

        try
        {
            string trimmed = input.Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return JsonSerializer.Deserialize<ConditionalActionModel>(trimmed) ?? new ConditionalActionModel();
            }

            // Uproszczony format: "target?ifTrue:ifFalse" lub "target?ifTrue"
            var model = new ConditionalActionModel();
            var parts = trimmed.Split('?', 2);
            model.Target = parts[0].Trim();

            if (parts.Length > 1)
            {
                var branches = parts[1].Split(':', 2);
                if (branches.Length > 0)
                {
                    ParseAction(branches[0], out var trueAction, out var trueParam);
                    model.IfTrueAction = trueAction;
                    model.IfTrueParameter = trueParam;
                }
                if (branches.Length > 1)
                {
                    ParseAction(branches[1], out var falseAction, out var falseParam);
                    model.IfFalseAction = falseAction;
                    model.IfFalseParameter = falseParam;
                }
            }
            return model;
        }
        catch
        {
            return new ConditionalActionModel();
        }
    }

    private static void ParseAction(string raw, out DeckActionType action, out string param)
    {
        action = DeckActionType.None;
        param = "";
        if (string.IsNullOrWhiteSpace(raw)) return;

        var sub = raw.Split('|', 2);
        if (Enum.TryParse<DeckActionType>(sub[0].Trim(), true, out var parsed))
        {
            action = parsed;
        }
        if (sub.Length > 1)
        {
            param = sub[1].Trim();
        }
    }
}
