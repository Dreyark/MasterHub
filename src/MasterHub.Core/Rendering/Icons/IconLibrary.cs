using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using SkiaSharp;
using Svg.Skia;

namespace MasterHub.Core.Rendering.Icons;

/// <summary>One icon of the library: its name (<c>house</c>) and the words it is found by.</summary>
public sealed record IconEntry(string Name, IReadOnlyList<string> Tags)
{
    /// <summary>The name in words (<c>arrow-big-up</c> → <c>arrow big up</c>), used in tooltips.</summary>
    public string Label => Name.Replace('-', ' ');
}

/// <summary>
/// What a key shows as its icon (T-21a): <c>lucide:&lt;name&gt;</c> for an icon of the bundled library,
/// <c>emoji:&lt;text&gt;</c> for an emoji or symbol, or a picture file.
/// </summary>
public readonly record struct IconRef(IconRefKind Kind, string Value)
{
    public const string LucidePrefix = "lucide:";
    public const string EmojiPrefix = "emoji:";

    public static IconRef Lucide(string name) => new(IconRefKind.Lucide, name);
    public static IconRef Emoji(string text) => new(IconRefKind.Emoji, text);
    public static IconRef File(string path) => new(IconRefKind.File, path);

    public static IconRef? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.StartsWith(LucidePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string name = text[LucidePrefix.Length..].Trim().ToLowerInvariant();
            return IconLibrary.IsValidName(name) ? Lucide(name) : null;
        }
        if (text.StartsWith(EmojiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string glyph = text[EmojiPrefix.Length..];
            return glyph.Length == 0 ? null : Emoji(glyph);
        }
        return File(text);
    }

    public override string ToString() => Kind switch
    {
        IconRefKind.Lucide => LucidePrefix + Value,
        IconRefKind.Emoji => EmojiPrefix + Value,
        _ => Value
    };
}

public enum IconRefKind { File, Lucide, Emoji }

public interface IIconLibrary
{
    /// <summary>Every icon, sorted by name.</summary>
    IReadOnlyList<IconEntry> All { get; }

    IconEntry? Find(string name);

    /// <summary>Icons that match the words typed, the best match first (name, then the start of the name, then a tag).</summary>
    IReadOnlyList<IconEntry> Search(string? query, int max = 60);

    /// <summary>The icon as a complete SVG document in one colour, with some air around it so it does not touch the key edge.</summary>
    string? BuildSvg(string name, string tintHex, int paddingPercent = 18);

    /// <summary>The icon drawn at a size (transparent background); the caller disposes the bitmap.</summary>
    SKBitmap? Render(string name, string tintHex, int size, int paddingPercent = 18);
}

/// <summary>
/// The bundled icon library (T-21a): <b>Lucide</b> (ISC licence, see <c>NOTICE.md</c>), 2 300+ outline icons, packed as one
/// gzip text resource (<c>name|tags|svg elements</c> per line) and read the first time it is used. Icons are drawn in one
/// colour by the existing SVG path (Svg.Skia), so a key can tint its icon to match its look.
/// </summary>
public sealed class IconLibrary : IIconLibrary
{
    private const string ResourceName = "MasterHub.Core.Rendering.Icons.lucide.txt.gz";

    private static readonly Lazy<IconLibrary> _shared = new(() => new IconLibrary());
    public static IconLibrary Shared => _shared.Value;

    private readonly Lazy<(List<IconEntry> list, Dictionary<string, (IconEntry entry, string body)> map)> _data;

    public IconLibrary() : this(null) { }

    /// <summary>For tests: the library text (<c>name|tags|elements</c> lines) instead of the bundled resource.</summary>
    public IconLibrary(string? libraryText)
    {
        _data = new(() => Load(libraryText));
    }

    public IReadOnlyList<IconEntry> All => _data.Value.list;

    public static bool IsValidName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 64 && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    public IconEntry? Find(string name) =>
        name != null && _data.Value.map.TryGetValue(name.Trim().ToLowerInvariant(), out var e) ? e.entry : null;

    public IReadOnlyList<IconEntry> Search(string? query, int max = 60)
    {
        var list = _data.Value.list;
        if (max <= 0) return Array.Empty<IconEntry>();
        string q = (query ?? "").Trim().ToLowerInvariant();
        if (q.Length == 0) return list.Take(max).ToList();

        string[] words = q.Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries);
        string joined = string.Join("-", words);
        var scored = new List<(int score, IconEntry entry)>();
        foreach (var entry in list)
        {
            int score = Score(entry, joined, words);
            if (score > 0) scored.Add((score, entry));
        }
        return scored
            .OrderByDescending(s => s.score)
            .ThenBy(s => s.entry.Name.Length)
            .ThenBy(s => s.entry.Name, StringComparer.Ordinal)
            .Take(max)
            .Select(s => s.entry)
            .ToList();
    }

    private static int Score(IconEntry entry, string joined, string[] words)
    {
        if (entry.Name == joined) return 1000;
        if (entry.Name.StartsWith(joined, StringComparison.Ordinal)) return 800;
        if (entry.Name.Contains(joined, StringComparison.Ordinal)) return 600;

        // every word must be found in the name or in a tag
        int total = 0;
        foreach (string w in words)
        {
            if (entry.Name.Split('-').Any(p => p.StartsWith(w, StringComparison.Ordinal))) total += 100;
            else if (entry.Name.Contains(w, StringComparison.Ordinal)) total += 60;
            else if (entry.Tags.Any(t => t.Equals(w, StringComparison.Ordinal))) total += 40;
            else if (entry.Tags.Any(t => t.Contains(w, StringComparison.Ordinal))) total += 20;
            else return 0;
        }
        return total;
    }

    public string? BuildSvg(string name, string tintHex, int paddingPercent = 18)
    {
        if (name == null || !_data.Value.map.TryGetValue(name.Trim().ToLowerInvariant(), out var item)) return null;
        string colour = NormalizeHex(tintHex);
        double pad = 24.0 * Math.Clamp(paddingPercent, 0, 40) / 100.0;
        string min = pad == 0 ? "0" : (-pad).ToString("0.##", CultureInfo.InvariantCulture);
        string size = (24 + 2 * pad).ToString("0.##", CultureInfo.InvariantCulture);
        return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"96\" height=\"96\" viewBox=\"" + min + " " + min + " " + size + " " + size + "\" " +
               "fill=\"none\" stroke=\"" + colour + "\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">" +
               item.body.Replace("currentColor", colour) + "</svg>";
    }

    public SKBitmap? Render(string name, string tintHex, int size, int paddingPercent = 18)
    {
        string? svgText = BuildSvg(name, tintHex, paddingPercent);
        return svgText == null ? null : SvgRasterizer.RasterizeSvgText(svgText, size, size);
    }

    /// <summary><c>#rrggbb</c> in lower case; anything else becomes white.</summary>
    public static string NormalizeHex(string? hex)
    {
        string h = (hex ?? "").Trim();
        if (h.StartsWith('#')) h = h[1..];
        if (h.Length == 8) h = h[2..];   // #aarrggbb → rrggbb
        if (h.Length == 3) h = string.Concat(h.Select(c => new string(c, 2)));
        return h.Length == 6 && h.All(Uri.IsHexDigit) ? "#" + h.ToLowerInvariant() : "#ffffff";
    }

    private static (List<IconEntry>, Dictionary<string, (IconEntry, string)>) Load(string? text)
    {
        var list = new List<IconEntry>();
        var map = new Dictionary<string, (IconEntry, string)>(StringComparer.Ordinal);
        try
        {
            text ??= ReadResource();
            if (text == null) return (list, map);
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int a = line.IndexOf('|');
                int b = a < 0 ? -1 : line.IndexOf('|', a + 1);
                if (b < 0) continue;
                string name = line[..a];
                if (!IsValidName(name) || map.ContainsKey(name)) continue;
                string body = line[(b + 1)..];
                // only drawing elements: the library is data, never a way to run anything
                if (body.Contains("<script", StringComparison.OrdinalIgnoreCase) || body.Contains("href", StringComparison.OrdinalIgnoreCase)) continue;
                var tags = line[(a + 1)..b].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(t => t.ToLowerInvariant()).ToArray();
                var entry = new IconEntry(name, tags);
                list.Add(entry);
                map[name] = (entry, body);
            }
            list.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
        }
        catch
        {
            // a broken resource leaves an empty library, never a crash
        }
        return (list, map);
    }

    private static string? ReadResource()
    {
        using var stream = typeof(IconLibrary).Assembly.GetManifestResourceStream(ResourceName);
        if (stream == null) return null;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
