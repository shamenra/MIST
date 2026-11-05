using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Selects a version definition only when it covers EVERY installed Steam
/// manifest and is the unique qualifying definition. This deliberately
/// rejects partial/ambiguous matches; a Steam build ID is a display-only
/// fallback until a trustworthy public version mapping exists.
///
/// No game files or databases are modified.
/// </summary>
public static class MISTSteamVersionMatcher
{
    public static bool TrySelectUnique(
        IEnumerable<ulong> installedManifestIds,
        IEnumerable<IEnumerable<ulong>> candidateVersionManifestSets,
        out int selectedIndex)
    {
        ArgumentNullException.ThrowIfNull(installedManifestIds);
        ArgumentNullException.ThrowIfNull(candidateVersionManifestSets);

        selectedIndex = -1;
        var installed = installedManifestIds.ToHashSet();
        if (installed.Count == 0 || installed.Contains(0))
            return false;

        var index = 0;
        foreach (var candidate in candidateVersionManifestSets)
        {
            if (candidate is not null)
            {
                var known = candidate.ToHashSet();
                if (known.Count != 0 && installed.IsSubsetOf(known))
                {
                    if (selectedIndex >= 0)
                    {
                        selectedIndex = -1; // Ambiguous: never choose arbitrarily.
                        return false;
                    }

                    selectedIndex = index;
                }
            }

            index++;
        }

        return selectedIndex >= 0;
    }
}
