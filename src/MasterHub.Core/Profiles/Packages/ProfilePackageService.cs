using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MasterHub.Core.Actions;

namespace MasterHub.Core.Profiles.Packages;

/// <summary>Visits every picture path of a profile (keys of every page and folder, wallpapers, the dial).</summary>
public static class ProfilePaths
{
    /// <summary>Calls <paramref name="rewrite"/> for each picture path; a returned value replaces the path (<c>null</c> clears it).</summary>
    public static void Rewrite(ProfileModel profile, Func<string, string?> rewrite)
    {
        string? Fix(string? value) => string.IsNullOrWhiteSpace(value) ? value : rewrite(value);

        void Keys(IEnumerable<KeyAssignmentModel>? keys)
        {
            if (keys == null) return;
            foreach (var k in keys)
            {
                k.ImagePath = Fix(k.ImagePath) ?? "";
                k.ActiveImagePath = Fix(k.ActiveImagePath) ?? "";
                k.CustomIconPath = Fix(k.CustomIconPath);
            }
        }

        Keys(profile.Keys);
        foreach (var page in profile.Pages ?? new()) { Keys(page.Keys); page.PanoramicWallpaperPath = Fix(page.PanoramicWallpaperPath); }
        foreach (var folder in profile.Folders ?? new()) Keys(folder.Keys);
        profile.PanoramicWallpaperPath = Fix(profile.PanoramicWallpaperPath);
        if (profile.Dial != null) profile.Dial.ImagePath = Fix(profile.Dial.ImagePath);
    }

    /// <summary>Removes every sound file path (sound keys and timer end sounds): sounds are never packed, and a path on someone else's PC means nothing.</summary>
    public static void ClearSounds(ProfileModel profile)
    {
        void Keys(IEnumerable<KeyAssignmentModel>? keys)
        {
            if (keys == null) return;
            foreach (var k in keys)
            {
                if (k.Sound != null) { k.Sound.FilePath = ""; }
                if (k.Timer != null) k.Timer.EndSoundPath = "";
            }
        }

        Keys(profile.Keys);
        foreach (var page in profile.Pages ?? new()) Keys(page.Keys);
        foreach (var folder in profile.Folders ?? new()) Keys(folder.Keys);
    }

    /// <summary>Every distinct non-empty picture path of the profile.</summary>
    public static IReadOnlyList<string> Collect(ProfileModel profile)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Rewrite(profile, p => { found.Add(p); return p; });
        return found.ToList();
    }
}

/// <summary>Something in an imported profile that can run a program, open a file or a web page, or type text.</summary>
/// <param name="Page">1-based page number, or the folder name.</param>
/// <param name="Key">1-based key number.</param>
/// <param name="Action">What the key does.</param>
/// <param name="Detail">The path, address or text it would use (shown to the user, never trusted).</param>
public sealed record ProgramRisk(string Page, int Key, DeckActionType Action, string Detail);

/// <summary>A package that was read and checked but not saved yet (so the user can be asked first).</summary>
public sealed class PackagePreview
{
    public required ProfileModel Profile { get; init; }

    /// <summary>Pictures to put into the store on commit: package name → bytes.</summary>
    public required IReadOnlyDictionary<string, byte[]> Images { get; init; }

    public required IReadOnlyList<ProgramRisk> Risks { get; init; }

    /// <summary>Pictures the profile referred to but the package did not contain (those keys keep their icon and label).</summary>
    public required int MissingPictures { get; init; }
}

/// <summary>Raised for a package that is not acceptable (bad zip, too big, unsafe names…).</summary>
public sealed class PackageException : Exception
{
    public PackageException(string message) : base(message) { }
}

/// <summary>
/// <c>.mhdeck</c> packages (T-09): a zip with <c>profile.json</c> and <c>images/&lt;sha256&gt;.&lt;ext&gt;</c>. Credentials are never in
/// it; sound files and programs are not packed; an import always creates a <b>new</b> profile id; unknown entries are
/// ignored; limits are 15 MB per picture, 100 MB unpacked and 1000 entries.
/// </summary>
public sealed partial class ProfilePackageService
{
    public const string Extension = ".mhdeck";
    public const long MaxPictureBytes = 15L * 1024 * 1024;
    public const long MaxUnpackedBytes = 100L * 1024 * 1024;
    public const int MaxEntries = 1000;
    public const long MaxProfileJsonBytes = 8L * 1024 * 1024;

    private const string ProfileEntry = "profile.json";
    private const string ImagesPrefix = "images/";

    private readonly IProfileService _profiles;
    private readonly IMediaStore _media;

    public ProfilePackageService(IProfileService profiles, IMediaStore media)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _media = media ?? throw new ArgumentNullException(nameof(media));
    }

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,128}$")]
    private static partial Regex SafeName();

    // ── export ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Writes the profile and its pictures into a package. Pictures that are missing are dropped (the key keeps icon and label).</summary>
    public void Export(string profileId, string packagePath)
    {
        // ExportProfileToJson strips every credential
        var profile = JsonSerializer.Deserialize<ProfileModel>(_profiles.ExportProfileToJson(profileId), JsonOptions)
                      ?? throw new PackageException("The profile could not be read.");

        ProfilePaths.ClearSounds(profile);               // sound files are not packed, so their paths do not travel either

        var pictures = new Dictionary<string, byte[]>(); // package name -> bytes
        ProfilePaths.Rewrite(profile, path =>
        {
            if (!MediaStore.IsPackable(path)) return null;           // videos, unknown formats: not packed
            string? local = _media.IsStorePath(path) ? path : _media.Import(path);
            if (local == null || !File.Exists(local)) return null;   // missing: dropped

            var info = new FileInfo(local);
            if (info.Length > MaxPictureBytes) return null;

            string name = Path.GetFileName(local);
            if (!SafeName().IsMatch(name)) return null;
            if (!pictures.ContainsKey(name)) pictures[name] = File.ReadAllBytes(local);
            return ImagesPrefix + name;
        });

        string? dir = Path.GetDirectoryName(packagePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        string tmp = packagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry(ProfileEntry, CompressionLevel.Optimal);
                using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                {
                    writer.Write(JsonSerializer.Serialize(profile, JsonOptions));
                }

                foreach (var (name, bytes) in pictures)
                {
                    var e = zip.CreateEntry(ImagesPrefix + name, CompressionLevel.NoCompression);
                    using var s = e.Open();
                    s.Write(bytes);
                }
            }
            File.Move(tmp, packagePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    // ── import ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads and checks a package without saving anything.</summary>
    public PackagePreview Inspect(string packagePath)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Package not found.", packagePath);

        ZipArchive zip;
        try { zip = ZipFile.OpenRead(packagePath); }
        catch (InvalidDataException) { throw new PackageException("This file is not a valid package."); }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries) throw new PackageException($"The package has more than {MaxEntries} entries.");

            long unpacked = 0;
            string? profileJson = null;
            var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName;

                // Only two kinds of entries are looked at; everything else (and every directory) is ignored.
                if (name == ProfileEntry)
                {
                    profileJson = Encoding.UTF8.GetString(ReadLimited(entry, MaxProfileJsonBytes, ref unpacked));
                }
                else if (name.StartsWith(ImagesPrefix, StringComparison.Ordinal) && name.Length > ImagesPrefix.Length)
                {
                    string file = name[ImagesPrefix.Length..];
                    // zip-slip: a name with a path separator, "..", a drive or odd characters is never used
                    if (!SafeName().IsMatch(file) || file.Contains("..") || file.StartsWith('.')) continue;
                    if (!MediaStore.IsPackable(file)) continue;

                    byte[] bytes = ReadLimited(entry, MaxPictureBytes, ref unpacked);
                    if (LooksLikePicture(bytes, MediaStore.NormalizeExtension(Path.GetExtension(file)))) images[file] = bytes;
                }
            }

            if (profileJson == null) throw new PackageException("The package has no profile.json.");

            ProfileModel profile;
            try
            {
                profile = JsonSerializer.Deserialize<ProfileModel>(profileJson, JsonOptions)
                          ?? throw new PackageException("The profile in the package is empty.");
            }
            catch (JsonException)
            {
                throw new PackageException("The profile in the package is not valid.");
            }

            ProfilePaths.ClearSounds(profile);   // a hand-made package must not point a key at a file on this PC

            // Pictures come back as paths in the local store; the ones the package lacks are dropped.
            int missing = 0;
            ProfilePaths.Rewrite(profile, path =>
            {
                if (path.StartsWith(ImagesPrefix, StringComparison.Ordinal) && images.ContainsKey(path[ImagesPrefix.Length..])) return path;
                missing++;
                return null;
            });

            return new PackagePreview
            {
                Profile = profile,
                Images = images,
                Risks = ProgramScanner.Scan(profile),
                MissingPictures = missing
            };
        }
    }

    /// <summary>Saves a checked package as a <b>new</b> profile (a snapshot is taken first by the profile service).</summary>
    public ProfileModel Commit(PackagePreview preview)
    {
        var profile = preview.Profile;

        // pictures into the store, paths rewritten to the local copies
        var stored = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, bytes) in preview.Images)
        {
            stored["images/" + name] = _media.ImportBytes(bytes, Path.GetExtension(name));
        }
        ProfilePaths.Rewrite(profile, path => stored.TryGetValue(path, out var local) ? local : null);

        // always a new identity: nothing in a package can replace an existing profile
        profile.Id = Guid.NewGuid().ToString("N");
        profile.IsDefault = false;
        profile.SmartRules = new List<SmartProfileRule>();   // linked apps belong to the author's PC
        profile.AutoMade = false;
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        return _profiles.ImportProfileFromJson(json);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>Reads an entry but never more than <paramref name="limit"/> bytes, and counts the real size (the header may lie).</summary>
    private static byte[] ReadLimited(ZipArchiveEntry entry, long limit, ref long unpackedTotal)
    {
        if (entry.Length > limit) throw new PackageException($"'{entry.FullName}' is larger than the allowed {limit / (1024 * 1024)} MB.");

        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            unpackedTotal += read;
            if (buffer.Length + read > limit) throw new PackageException($"'{entry.FullName}' is larger than the allowed {limit / (1024 * 1024)} MB.");
            if (unpackedTotal > MaxUnpackedBytes) throw new PackageException("The package unpacks to more than 100 MB.");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>Checks that the bytes really are the kind of picture the name claims (a renamed program is not a picture).</summary>
    internal static bool LooksLikePicture(byte[] b, string extension)
    {
        if (b.Length < 4) return false;
        return extension switch
        {
            ".png" => b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47,
            ".jpg" => b[0] == 0xFF && b[1] == 0xD8,
            ".gif" => b[0] == 'G' && b[1] == 'I' && b[2] == 'F',
            ".webp" => b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P',
            ".svg" => IsSafeSvg(b),
            _ => false
        };
    }

    private static bool IsSafeSvg(byte[] b)
    {
        string text = Encoding.UTF8.GetString(b, 0, Math.Min(b.Length, 64 * 1024)).TrimStart('﻿', ' ', '\r', '\n', '\t');
        if (!(text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) || text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))) return false;
        string all = Encoding.UTF8.GetString(b);
        return !all.Contains("<script", StringComparison.OrdinalIgnoreCase) && !all.Contains("javascript:", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Lists what in a profile can execute something, so an import asks first.</summary>
public static class ProgramScanner
{
    private static readonly HashSet<DeckActionType> Executing = new()
    {
        DeckActionType.OpenApplication, DeckActionType.OpenFile, DeckActionType.OpenWebsite, DeckActionType.CloseApplication,
        DeckActionType.TextPaste, DeckActionType.Hotkey, DeckActionType.HotkeySwitch, DeckActionType.MacroPlay,
        DeckActionType.StreamDeckPluginAction, DeckActionType.MultiAction, DeckActionType.ConditionalAction,
        DeckActionType.TypeText, DeckActionType.HttpRequest   // T-17: types text / calls an address
    };

    public static IReadOnlyList<ProgramRisk> Scan(ProfileModel profile)
    {
        var risks = new List<ProgramRisk>();

        void Keys(string page, IEnumerable<KeyAssignmentModel>? keys)
        {
            if (keys == null) return;
            foreach (var k in keys)
            {
                Check(risks, page, k.Index + 1, k.Action, k.ActionParameter, k);
                Check(risks, page, k.Index + 1, k.Gestures?.Hold?.Action ?? DeckActionType.None, k.Gestures?.Hold?.ActionParameter, null);
                Check(risks, page, k.Index + 1, k.Gestures?.Double?.Action ?? DeckActionType.None, k.Gestures?.Double?.ActionParameter, null);

                // a live-value key may run a command or call an address with a token
                if (k.Action == DeckActionType.LiveValue && k.LiveValue is { } live)
                {
                    if (live.Kind == LiveValues.LiveValueSourceKind.Command && !string.IsNullOrWhiteSpace(live.Command))
                        risks.Add(new ProgramRisk(page, k.Index + 1, k.Action, "command: " + Shorten(live.Command)));
                }
            }
        }

        // The top-level keys mirror page 1, but a hand-made package may fill only one of them: scan both, list each thing once.
        Keys("1", profile.Keys);
        for (int i = 0; i < (profile.Pages?.Count ?? 0); i++) Keys((i + 1).ToString(), profile.Pages![i].Keys);

        foreach (var folder in profile.Folders ?? new()) Keys(folder.Name, folder.Keys);
        return risks.Distinct().ToList();
    }

    private static void Check(List<ProgramRisk> risks, string page, int key, DeckActionType action, string? parameter, KeyAssignmentModel? legacy)
    {
        if (!Executing.Contains(action)) return;

        string detail = parameter ?? "";
        if (string.IsNullOrWhiteSpace(detail) && legacy != null)
            detail = legacy.ApplicationPath ?? legacy.Url ?? legacy.TextContent ?? "";

        if (action == DeckActionType.ConditionalAction)
        {
            var model = ConditionalActionModel.Parse(parameter);
            bool runs = Executing.Contains(model.IfTrueAction) || Executing.Contains(model.IfFalseAction);
            if (!runs) return;
            detail = $"{model.IfTrueAction}: {model.IfTrueParameter} / {model.IfFalseAction}: {model.IfFalseParameter}";
        }

        risks.Add(new ProgramRisk(page, key, action, Shorten(detail)));
    }

    private static string Shorten(string text) => text.Length > 120 ? text[..120] + "…" : text;
}
