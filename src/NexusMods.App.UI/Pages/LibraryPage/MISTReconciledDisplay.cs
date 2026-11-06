// MIST-006-14B2B8.4 — read-only, provenance-bound Library presentation of
// a previously reviewed local reconciliation proposal. NOT an ownership map.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NexusMods.DataModel.ModDiscovery;

namespace NexusMods.App.UI.Pages.LibraryPage;

internal static class MISTReconciledDisplay
{
    private const string Schema = "mist-conservative-reconciliation/v1";
    private const string Prefix = "MIST-006-14B2B8.3-reconciliation-";
    private const long MaxReportBytes = 8 * 1024 * 1024;
    private const int MaxDisplayFiles = 75_000;

    // Calling this has no writes, API calls, DB calls, or game-file reads.
    // Every failure falls back to the ORIGINAL scanner grouping.
    public static MISTDiscoveredMod[]? TryLoad(
        MISTModDiscoveryPipeline.Report discovery, string discoveryReportPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(discoveryReportPath)) return null;
            var source = Path.GetFullPath(discoveryReportPath);
            var directory = Path.GetDirectoryName(source);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory)) return null;
            foreach (var path in Directory.EnumerateFiles(directory, Prefix + "*.json")
                         .OrderByDescending(File.GetLastWriteTimeUtc).Take(24))
            {
                var info = new FileInfo(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    info.Length <= 0 || info.Length > MaxReportBytes) continue;
                try
                {
                    using var stream = File.OpenRead(path);
                    using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
                    var result = FromReport(json.RootElement, discovery, source);
                    if (result is not null) return result;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                           JsonException or KeyNotFoundException or InvalidOperationException or
                                           FormatException or ArgumentException or OverflowException)
                {
                    // The proposal is optional; an invalid/stale JSON cannot break the Library.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   ArgumentException or NotSupportedException)
        {
            // The original discovery display remains available.
        }
        return null;
    }

    private static MISTDiscoveredMod[]? FromReport(
        JsonElement json, MISTModDiscoveryPipeline.Report discovery, string source)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            json.GetProperty("schema").GetString() != Schema ||
            json.GetProperty("game").GetString() != "Cyberpunk 2077" ||
            !string.Equals(json.GetProperty("gameRoot").GetString(),
                discovery.Inventory.GameRoot, StringComparison.Ordinal) ||
            !string.Equals(json.GetProperty("sourceDiscovery").GetString(),
                source, StringComparison.Ordinal)) return null;

        var safety = json.GetProperty("safety");
        if (!safety.GetProperty("readOnly").GetBoolean() ||
            safety.GetProperty("libraryChanged").GetBoolean() ||
            safety.GetProperty("adoptionPerformed").GetBoolean() ||
            safety.GetProperty("restoreOrDeleteAllowed").GetBoolean()) return null;

        var groups = discovery.Inventory.CandidateGroups;
        var byGroup = groups.ToDictionary(g => g.Key, StringComparer.Ordinal);
        if (byGroup.Count != groups.Count) return null;
        var groupedCount = groups.Sum(g => g.Files.Count);
        var ungroupedCount = discovery.Inventory.UnattributedFiles.Count;
        if (groupedCount + ungroupedCount > MaxDisplayFiles) return null;

        // Precise source inventory from the actual discovery report. JSON cannot
        // fabricate paths, change sizes, omit files, or move a file to a new group.
        var inventory = new Dictionary<string, (string Group, long Size)>(StringComparer.Ordinal);
        foreach (var g in groups)
        foreach (var file in g.Files)
        {
            if (!SafePath(file.Path) || !inventory.TryAdd(file.Path, (g.Key, file.SizeBytes)))
                return null;
        }
        var unassociated = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var file in discovery.Inventory.UnattributedFiles)
        {
            if (!SafePath(file.Path) || inventory.ContainsKey(file.Path) ||
                !unassociated.TryAdd(file.Path, file.SizeBytes)) return null;
        }
        if (inventory.Count != groupedCount) return null;

        var accounting = json.GetProperty("accounting");
        if (accounting.GetProperty("originalGroups").GetInt32() != groups.Count ||
            accounting.GetProperty("originalGroupedFiles").GetInt32() != groupedCount ||
            accounting.GetProperty("proposedGroupedFiles").GetInt32() != groupedCount ||
            accounting.GetProperty("unattributedFilesPreserved").GetInt32() != ungroupedCount ||
            accounting.GetProperty("allUniqueInventoriedPaths").GetInt32() != inventory.Count + unassociated.Count ||
            accounting.GetProperty("discoveryReportedInstalledFileCount").GetInt32() !=
                discovery.Inventory.InstalledFileCount ||
            !accounting.GetProperty("discoveryCountMatches").GetBoolean() ||
            accounting.GetProperty("pathsDropped").GetInt32() != 0 ||
            accounting.GetProperty("duplicatedPaths").GetInt32() != 0) return null;

        var rows = new List<MISTDiscoveredMod>();
        var seenGroups = new HashSet<string>(StringComparer.Ordinal);
        var seenFiles = new HashSet<string>(StringComparer.Ordinal);
        var strongCount = 0;
        var entries = json.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() > groups.Count ||
            entries.GetArrayLength() != accounting.GetProperty("proposedEntries").GetInt32()) return null;

        foreach (var entry in entries.EnumerateArray())
        {
            var memberKeys = entry.GetProperty("memberGroupKeys").EnumerateArray()
                .Select(x => x.GetString() ?? string.Empty).ToArray();
            if (memberKeys.Length == 0 || memberKeys.Any(k => k.Length == 0 ||
                !byGroup.ContainsKey(k) || !seenGroups.Add(k))) return null;

            // Reconstructed entries are STRICTLY display associations, never managed objects.
            if (entry.GetProperty("ownership").GetString() != "unmanaged" ||
                entry.GetProperty("managementEligible").GetBoolean() ||
                entry.GetProperty("installedVersion").ValueKind != JsonValueKind.Null ||
                entry.GetProperty("versionStatus").GetString() != "unknown") return null;

            var strong = entry.GetProperty("associationStatus").GetString() == "strong-display-proposal";
            if (strong)
            {
                strongCount++;
                if (memberKeys.Length < 2 ||
                    entry.GetProperty("identityStatus").GetString() != "probable-path-based" ||
                    entry.GetProperty("candidateNexusModId").GetInt64() <= 0 ||
                    entry.GetProperty("nexusReleaseIdsContainingAllQueriedGroupPaths")
                        .GetArrayLength() < 2) return null;
            }
            else if (memberKeys.Length != 1 ||
                     entry.GetProperty("associationStatus").GetString() != "original-discovery-group")
                return null;

            var files = entry.GetProperty("allFiles");
            if (files.ValueKind != JsonValueKind.Array || files.GetArrayLength() == 0 ||
                files.GetArrayLength() != entry.GetProperty("fileCount").GetInt32()) return null;
            var paths = new List<string>(files.GetArrayLength());
            long size = 0;
            foreach (var file in files.EnumerateArray())
            {
                var path = file.GetProperty("path").GetString();
                var groupKey = file.GetProperty("originalGroupKey").GetString();
                var bytes = file.GetProperty("sizeBytes").GetInt64();
                if (path is null || groupKey is null || !SafePath(path) ||
                    !inventory.TryGetValue(path, out var expected) ||
                    !memberKeys.Contains(groupKey, StringComparer.Ordinal) ||
                    expected.Group != groupKey || expected.Size != bytes ||
                    !seenFiles.Add(path)) return null;
                paths.Add(path);
                size = checked(size + Math.Max(0, bytes));
            }
            var displayName = entry.GetProperty("displayName").GetString();
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 150 ||
                displayName.Any(char.IsControl)) return null;
            var key = entry.GetProperty("entryId").GetString();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 150 ||
                key.Any(char.IsControl)) return null;

            var versionHints = memberKeys.SelectMany(member => discovery.IdentityHints
                    .Where(h => h.GroupKey == member && h.Kind == "embedded-version")
                    .Select(h => h.Value))
                .Distinct(StringComparer.Ordinal).Take(3).ToArray();
            var versionText = versionHints.Length == 0 ? "Version unknown" :
                "Version hint: " + string.Join(", ", versionHints) + " (unverified)";
            var recognition = strong
                ? $"Probable Nexus #{entry.GetProperty("candidateNexusModId").GetInt64()} · " +
                  $"Associated groups: {string.Join(", ", memberKeys.Select(k => byGroup[k].SuggestedName))} · " +
                  "Unmanaged (display only)"
                : "Local discovery group · Unmanaged · Nexus identity not verified";
            rows.Add(new MISTDiscoveredMod(key, displayName,
                $"{paths.Count} files", versionText, recognition,
                string.Join("\n", paths), FormatSize(size), size));
        }

        if (strongCount != accounting.GetProperty("strongCombinedProposals").GetInt32() ||
            seenGroups.Count != groups.Count || seenFiles.Count != inventory.Count) return null;

        var unassociatedJson = json.GetProperty("unattributedFiles");
        if (unassociatedJson.ValueKind != JsonValueKind.Array ||
            unassociatedJson.GetArrayLength() != unassociated.Count) return null;
        var verifiedUnassociated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in unassociatedJson.EnumerateArray())
        {
            var path = file.GetProperty("path").GetString();
            var size = file.GetProperty("sizeBytes").GetInt64();
            if (path is null || !unassociated.TryGetValue(path, out var expectedSize) ||
                expectedSize != size || !verifiedUnassociated.Add(path)) return null;
        }
        if (verifiedUnassociated.Count != unassociated.Count) return null;
        if (unassociated.Count > 0)
        {
            rows.Add(new MISTDiscoveredMod("mist-unassociated-display-only",
                "Unassociated files (review only)", $"{unassociated.Count} files",
                "Version unknown", "No mod identity · Unmanaged · Not eligible for management",
                string.Join("\n", unassociated.Keys),
                FormatSize(unassociated.Values.Sum(x => Math.Max(0L, x))),
                unassociated.Values.Sum(x => Math.Max(0L, x))));
        }
        return rows.ToArray();
    }

    private static bool SafePath(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 2048 ||
            value.StartsWith('/') || value.Contains('\\') || value.Any(char.IsControl)) return false;
        return value.Split('/').All(part => part.Length > 0 && part != "." && part != "..");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var magnitude = (double)bytes;
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" };
        var index = 0;
        while (magnitude >= 1024 && index < units.Length - 1)
        {
            magnitude /= 1024;
            index++;
        }
        return $"{magnitude:0.#} {units[index]}";
    }
}