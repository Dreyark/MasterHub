using System;
using System.Collections.Generic;
using System.Linq;
using MasterHub.Core.Actions;
using MasterHub.Core.Profiles;

namespace MasterHub.Core.Integrations.StreamDeck;

/// <summary>One key (or dial) that uses a plugin.</summary>
public sealed record PluginUsage(string ProfileName, string Page, int Key);

/// <summary>
/// Which keys use a plugin (T-31 c), so "Uninstall" can say what will stop working before it removes anything.
/// </summary>
public static class PluginUsageScanner
{
    public static IReadOnlyList<PluginUsage> Find(IEnumerable<ProfileModel> profiles, string pluginUuid)
    {
        var found = new List<PluginUsage>();
        if (string.IsNullOrWhiteSpace(pluginUuid)) return found;

        foreach (var profile in profiles)
        {
            void Keys(string page, IEnumerable<KeyAssignmentModel>? keys)
            {
                foreach (var k in keys ?? Enumerable.Empty<KeyAssignmentModel>())
                {
                    if (k.Action == DeckActionType.StreamDeckPluginAction &&
                        string.Equals(k.StreamDeckPluginUuid, pluginUuid, StringComparison.OrdinalIgnoreCase))
                        found.Add(new PluginUsage(profile.Name, page, k.Index + 1));
                }
            }

            Keys("1", profile.Keys);
            for (int i = 0; i < (profile.Pages?.Count ?? 0); i++) Keys((i + 1).ToString(), profile.Pages![i].Keys);
            foreach (var folder in profile.Folders ?? new()) Keys(folder.Name, folder.Keys);
        }
        return found.Distinct().ToList();
    }
}
