using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MasterHub.Core.Profiles.Backup;

/// <inheritdoc cref="IBackupService"/>
public sealed class BackupService : IBackupService
{
    /// <summary>Newest snapshots of any kind that are kept (older ones are pruned).</summary>
    public const int MaxSnapshots = 30;

    private const string TimestampFormat = "yyyy-MM-dd_HH-mm-ss";

    private readonly string _profilesDirectory;
    private readonly string _settingsFilePath;
    private readonly Func<DateTime> _now;
    private readonly Func<string?> _mirrorPath;
    private readonly object _lock = new();

    public BackupService(string baseDirectory, Func<DateTime>? now = null, Func<string?>? mirrorPath = null)
    {
        BackupsDirectory = Path.Combine(baseDirectory, "backups");
        _profilesDirectory = Path.Combine(baseDirectory, "profiles");
        _settingsFilePath = Path.Combine(baseDirectory, "settings.json");
        _now = now ?? (() => DateTime.Now);
        _mirrorPath = mirrorPath ?? (() => null);
    }

    public string BackupsDirectory { get; }

    public BackupInfo? CreateSnapshot(string reason)
    {
        lock (_lock)
        {
            bool hasSettings = File.Exists(_settingsFilePath);
            string[] profileFiles = Directory.Exists(_profilesDirectory)
                ? Directory.GetFiles(_profilesDirectory, "*.json")
                : Array.Empty<string>();
            if (!hasSettings && profileFiles.Length == 0) return null;

            string cleanReason = SanitizeReason(reason);
            DateTime stamp = _now();
            string folderName = UniqueName($"{stamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)} {cleanReason}");
            string target = Path.Combine(BackupsDirectory, folderName);

            try
            {
                Directory.CreateDirectory(Path.Combine(target, "profiles"));
                if (hasSettings) File.Copy(_settingsFilePath, Path.Combine(target, "settings.json"), overwrite: true);
                foreach (string file in profileFiles)
                {
                    File.Copy(file, Path.Combine(target, "profiles", Path.GetFileName(file)), overwrite: true);
                }
            }
            catch
            {
                TryDeleteDirectory(target);
                return null;
            }

            Mirror(target, folderName);
            Prune();
            return ToInfo(target);
        }
    }

    public BackupInfo? EnsureDailySnapshot()
    {
        var newest = List().FirstOrDefault();
        if (newest != null && _now() - newest.CreatedLocal < TimeSpan.FromHours(24)) return null;
        return CreateSnapshot("daily");
    }

    public IReadOnlyList<BackupInfo> List()
    {
        lock (_lock)
        {
            if (!Directory.Exists(BackupsDirectory)) return Array.Empty<BackupInfo>();

            var result = new List<BackupInfo>();
            foreach (string dir in Directory.GetDirectories(BackupsDirectory))
            {
                try { result.Add(ToInfo(dir)); } catch { /* unreadable folder: skip */ }
            }

            return result.OrderByDescending(b => b.CreatedLocal).ThenByDescending(b => b.Name, StringComparer.Ordinal).ToList();
        }
    }

    public bool Restore(string name)
    {
        string? source = ResolveSnapshot(name);
        if (source == null) return false;

        lock (_lock)
        {
            // A restore can itself be undone.
            CreateSnapshot("before-restore");

            Directory.CreateDirectory(_profilesDirectory);
            foreach (string existing in Directory.GetFiles(_profilesDirectory, "*.json"))
            {
                File.Delete(existing);
            }

            string profilesSource = Path.Combine(source, "profiles");
            if (Directory.Exists(profilesSource))
            {
                foreach (string file in Directory.GetFiles(profilesSource, "*.json"))
                {
                    // Names inside a snapshot are untrusted too: only plain file names are copied.
                    string fileName = Path.GetFileName(file);
                    if (!ProfileService.IsSafeProfileId(Path.GetFileNameWithoutExtension(fileName))) continue;
                    File.Copy(file, Path.Combine(_profilesDirectory, fileName), overwrite: true);
                }
            }

            string settingsSource = Path.Combine(source, "settings.json");
            if (File.Exists(settingsSource))
            {
                string tmp = $"{_settingsFilePath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.Copy(settingsSource, tmp, overwrite: true);
                    File.Move(tmp, _settingsFilePath, overwrite: true);
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }

            return true;
        }
    }

    public bool Delete(string name)
    {
        string? source = ResolveSnapshot(name);
        if (source == null) return false;
        lock (_lock)
        {
            return TryDeleteDirectory(source);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Maps a snapshot name to its folder, rejecting anything that is not a direct child of the backups folder.</summary>
    private string? ResolveSnapshot(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        string path = Path.GetFullPath(Path.Combine(BackupsDirectory, name));
        string root = Path.GetFullPath(BackupsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return null;
        return Directory.Exists(path) ? path : null;
    }

    internal static string SanitizeReason(string reason)
    {
        var chars = (reason ?? "").Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        string clean = new string(chars);
        if (clean.Length == 0) clean = "manual";
        return clean.Length > 30 ? clean[..30] : clean;
    }

    private string UniqueName(string baseName)
    {
        string name = baseName;
        for (int i = 2; Directory.Exists(Path.Combine(BackupsDirectory, name)); i++)
        {
            name = $"{baseName} ({i})";
        }
        return name;
    }

    private static BackupInfo ToInfo(string dir)
    {
        string name = Path.GetFileName(dir);
        DateTime created = Directory.GetCreationTime(dir);
        string reason = name;

        if (name.Length >= TimestampFormat.Length &&
            DateTime.TryParseExact(name[..TimestampFormat.Length], TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            created = parsed;
            reason = name.Length > TimestampFormat.Length ? name[(TimestampFormat.Length + 1)..] : "";
            int counter = reason.LastIndexOf(" (", StringComparison.Ordinal);
            if (counter > 0 && reason.EndsWith(')')) reason = reason[..counter];
        }

        string profilesDir = Path.Combine(dir, "profiles");
        int count = Directory.Exists(profilesDir) ? Directory.GetFiles(profilesDir, "*.json").Length : 0;
        return new BackupInfo(name, dir, created, reason, count);
    }

    private void Prune()
    {
        try
        {
            foreach (var old in List().Skip(MaxSnapshots))
            {
                TryDeleteDirectory(old.FullPath);
            }
        }
        catch { /* pruning is best effort */ }
    }

    private void Mirror(string snapshotPath, string folderName)
    {
        try
        {
            string? mirror = _mirrorPath();
            if (string.IsNullOrWhiteSpace(mirror)) return;

            string destination = Path.Combine(mirror, folderName);
            CopyDirectory(snapshotPath, destination);
        }
        catch { /* the mirror (OneDrive/Dropbox folder) may be offline: never fail the snapshot */ }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
        foreach (string dir in Directory.GetDirectories(source))
        {
            CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
