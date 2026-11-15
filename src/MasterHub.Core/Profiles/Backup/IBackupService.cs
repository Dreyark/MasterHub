using System;
using System.Collections.Generic;

namespace MasterHub.Core.Profiles.Backup;

/// <summary>One snapshot folder in <c>%APPDATA%\MasterHub\backups\</c>.</summary>
/// <param name="Name">Folder name (also the id used for restore), e.g. <c>2026-11-13_09-30-00 daily</c>.</param>
/// <param name="FullPath">Absolute folder path.</param>
/// <param name="CreatedLocal">Time the snapshot was taken (parsed from the folder name).</param>
/// <param name="Reason">Why it was taken (<c>daily</c>, <c>before-delete</c>, <c>manual</c>…).</param>
/// <param name="ProfileCount">Number of profile files inside.</param>
public sealed record BackupInfo(string Name, string FullPath, DateTime CreatedLocal, string Reason, int ProfileCount);

/// <summary>
/// Versioned snapshots of <c>settings.json</c> and <c>profiles\*.json</c> (ROADMAP OPP-07 / B-09).
/// Snapshots are plain file copies, so they can also be mirrored to a OneDrive/Dropbox folder without any cloud SDK.
/// Note: DPAPI blobs (<c>enc:v1:</c>) only decrypt for the same Windows user on the same machine – restoring on another
/// PC leaves the integrations unauthenticated.
/// </summary>
public interface IBackupService
{
    /// <summary>Folder that holds the snapshots.</summary>
    string BackupsDirectory { get; }

    /// <summary>Takes a snapshot. Returns <c>null</c> when there is nothing to back up or the copy failed.</summary>
    BackupInfo? CreateSnapshot(string reason);

    /// <summary>Takes a <c>daily</c> snapshot unless the newest one is younger than 24 hours.</summary>
    BackupInfo? EnsureDailySnapshot();

    /// <summary>Snapshots, newest first.</summary>
    IReadOnlyList<BackupInfo> List();

    /// <summary>
    /// Restores a snapshot: first takes a <c>before-restore</c> snapshot (so a restore can itself be undone), then
    /// replaces the whole profile set and <c>settings.json</c>. Profiles created after the snapshot disappear.
    /// Returns <c>false</c> for an unknown or unsafe name.
    /// </summary>
    bool Restore(string name);

    /// <summary>Deletes one snapshot folder (unsafe names are ignored).</summary>
    bool Delete(string name);
}
