using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasterHub.Core.Macros;

/// <summary>Trwały zapis biblioteki makr w %APPDATA%\MasterHub\macros.json (zapis atomowy).</summary>
public sealed class MacroStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _path;
    private readonly object _lock = new();

    public MacroStore(string? baseDirectory = null)
    {
        string dir = baseDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MasterHub");
        _path = Path.Combine(dir, "macros.json");
    }

    public string FilePath => _path;

    public MacroLibrary Load()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_path)) return new MacroLibrary();
                var lib = JsonSerializer.Deserialize<MacroLibrary>(File.ReadAllText(_path), Json) ?? new MacroLibrary();
                return Sanitize(lib);
            }
            catch
            {
                // Uszkodzony plik: zachowujemy kopię do ręcznego odzyskania i startujemy z pustą biblioteką.
                try { File.Copy(_path, _path + ".corrupt", overwrite: true); } catch { }
                return new MacroLibrary();
            }
        }
    }

    public void Save(MacroLibrary library)
    {
        lock (_lock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            string tmp = $"{_path}.{Guid.NewGuid():N}.tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(library, Json));
                File.Move(tmp, _path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }

    /// <summary>Normalizuje wczytane dane (limity, brakujące id, wiązania do nieistniejących makr).</summary>
    internal static MacroLibrary Sanitize(MacroLibrary lib)
    {
        lib.Macros ??= new();
        lib.Bindings ??= new();
        foreach (var m in lib.Macros)
        {
            if (string.IsNullOrWhiteSpace(m.Id)) m.Id = Guid.NewGuid().ToString("N");
            m.Name = string.IsNullOrWhiteSpace(m.Name) ? "Makro" : m.Name;
            m.Steps ??= new();
            m.RepeatCount = Math.Clamp(m.RepeatCount, 1, MacroEngine.MaxRepeatCount);
            m.RepeatDelayMs = Math.Clamp(m.RepeatDelayMs, 0, 600_000);
            foreach (var s in m.Steps)
            {
                s.DelayMs = Math.Clamp(s.DelayMs, 0, 600_000);
                s.Key = Math.Clamp(s.Key, 0, 0xFF);
            }
        }
        var ids = lib.Macros.Select(m => m.Id).ToHashSet();
        lib.Bindings = lib.Bindings
            .Where(b => b != null && ids.Contains(b.MacroId))
            // lewy i prawy przycisk nie mogą być wyzwalaczami (zablokowałoby to normalne klikanie myszą)
            .Where(b => !(b.TriggerKind == MacroTriggerKind.MouseButton &&
                          b.Code is (int)MacroMouseButton.Left or (int)MacroMouseButton.Right))
            .ToList();
        return lib;
    }
}
