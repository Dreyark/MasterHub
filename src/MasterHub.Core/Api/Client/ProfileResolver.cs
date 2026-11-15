using System;
using System.Collections.Generic;
using System.Linq;

namespace MasterHub.Core.Api.Client;

/// <summary>Finds a profile from what a person types: its id, its name, the start of its name, or a part of it.</summary>
public static class ProfileResolver
{
    /// <summary>
    /// Order of the lookup: exact id, exact name (any case), the only name that starts with the text, the only name that contains it.
    /// A text that fits several profiles at one step is refused with their names: guessing which one was meant would switch the deck
    /// to the wrong profile.
    /// </summary>
    /// <returns>The profile, or <c>null</c> with <paramref name="problem"/> saying why not.</returns>
    public static ApiProfile? Resolve(IReadOnlyList<ApiProfile> profiles, string? query, out string? problem)
    {
        problem = null;
        string text = (query ?? "").Trim();
        if (text.Length == 0)
        {
            problem = "Say which profile: its name or its id.";
            return null;
        }

        var byId = profiles.FirstOrDefault(p => string.Equals(p.Id, text, StringComparison.Ordinal));
        if (byId != null) return byId;

        foreach (var fits in new Func<ApiProfile, bool>[]
        {
            p => string.Equals(p.Name, text, StringComparison.OrdinalIgnoreCase),
            p => p.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase),
            p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
        })
        {
            var matching = profiles.Where(fits).ToList();
            if (matching.Count == 1) return matching[0];
            if (matching.Count > 1)
            {
                problem = $"\"{text}\" fits more than one profile: {string.Join(", ", matching.Select(p => $"\"{p.Name}\""))}. Use the exact name or the id.";
                return null;
            }
        }

        problem = $"There is no profile \"{text}\". Profiles: {string.Join(", ", profiles.Select(p => $"\"{p.Name}\""))}.";
        return null;
    }
}
