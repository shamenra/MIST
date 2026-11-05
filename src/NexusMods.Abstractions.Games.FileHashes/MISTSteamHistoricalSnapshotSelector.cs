using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Resolve a group of retained Steam hash database snapshots without
/// conflating different original-file baselines or conflicting public versions.
/// Candidates are newest-first. A version label is optional; an unlabelled
/// but complete snapshot can still supply original-file records.
/// </summary>
public static class MISTSteamHistoricalSnapshotSelector
{
    public static bool TrySelect(
        IReadOnlyList<(string Fingerprint, string? Version)> candidates,
        out int selectedIndex, out string? publicVersion)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        selectedIndex = -1;
        publicVersion = null;
        if (candidates.Count == 0) return false;

        var firstFingerprint = candidates[0].Fingerprint;
        if (string.IsNullOrWhiteSpace(firstFingerprint) ||
            candidates.Any(c => c.Fingerprint != firstFingerprint))
            return false;

        var labels = candidates
            .Select(c => c.Version)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (labels.Length > 1) return false;

        publicVersion = labels.Length == 1 ? labels[0] : null;
        var resolvedVersion = publicVersion;
        selectedIndex = resolvedVersion is null ? 0 :
            candidates.ToList().FindIndex(c => c.Version == resolvedVersion);
        return selectedIndex >= 0;
    }
}
