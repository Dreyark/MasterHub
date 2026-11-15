using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace MasterHub.Core.Profiles.Backup;

/// <summary>
/// One step of the profile-schema upgrade chain. <see cref="Migrate"/> works on the raw JSON object (so it can rename or
/// move fields that no longer exist on <see cref="ProfileModel"/>); the migrator sets <c>SchemaVersion</c> afterwards.
/// Every migration must be covered by a test with a frozen old-JSON fixture.
/// </summary>
public interface IProfileMigration
{
    /// <summary>The schema version this migration upgrades <b>from</b> (it produces <c>FromVersion + 1</c>).</summary>
    int FromVersion { get; }

    void Migrate(JsonObject profile);
}

/// <summary>Applies the <see cref="IProfileMigration"/> chain to a profile file read from disk.</summary>
public sealed class ProfileMigrator
{
    /// <summary>Schema version written by this build. Files without the field are version 1.</summary>
    public const int CurrentVersion = 1;

    public const string VersionProperty = "SchemaVersion";

    /// <summary>The migrations shipped with the app (none yet: version 1 is the first versioned schema).</summary>
    public static ProfileMigrator Default { get; } = new(Array.Empty<IProfileMigration>(), CurrentVersion);

    private readonly IReadOnlyList<IProfileMigration> _migrations;
    private readonly int _targetVersion;

    public ProfileMigrator(IEnumerable<IProfileMigration> migrations, int targetVersion)
    {
        _migrations = migrations.OrderBy(m => m.FromVersion).ToList();
        _targetVersion = targetVersion;
    }

    public int TargetVersion => _targetVersion;

    /// <summary>Version stored in the file (1 when missing – every file written before versioning existed).</summary>
    public static int ReadVersion(JsonObject profile)
    {
        if (profile.TryGetPropertyValue(VersionProperty, out var node) && node is JsonValue value && value.TryGetValue<int>(out int v) && v >= 1)
            return v;
        return 1;
    }

    public bool NeedsMigration(JsonObject profile) => ReadVersion(profile) < _targetVersion;

    /// <summary>Upgrades <paramref name="profile"/> in place. Returns <c>true</c> when anything changed.</summary>
    public bool Apply(JsonObject profile)
    {
        int version = ReadVersion(profile);
        bool changed = false;

        while (version < _targetVersion)
        {
            var step = _migrations.FirstOrDefault(m => m.FromVersion == version)
                       ?? throw new InvalidOperationException($"No profile migration from schema version {version}.");
            step.Migrate(profile);
            version++;
            profile[VersionProperty] = version;
            changed = true;
        }

        return changed;
    }
}
