using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NexusMods.DataModel.ModDiscovery;

/// <summary>
/// MIST-006-14B2B: read-only discovery coordinator. Each module only receives a
/// game-scoped discovery context and returns evidence. It cannot request a sync,
/// create a loadout, access another manager, or write into the game directory.
/// </summary>
public static class MISTModDiscoveryPipeline
{
    public sealed record EvidenceHint(string GroupKey, string Kind, string Value,
        string RelativeSourcePath, string Reliability);

    public sealed record ContentFingerprint(string GroupKey, string RelativeSourcePath,
        long SizeBytes, string Sha256, string Meaning);

    public sealed record GroupRelation(string FirstKey, string SecondKey, string Relation,
        string Explanation);

    public sealed record ModuleSummary(string Module, string Status, int EvidenceCount,
        string Note);

    public sealed record Report(string Schema, MISTScopedCyberpunkDiscovery.ScopedDiscoveryReport Inventory,
        IReadOnlyList<EvidenceHint> IdentityHints,
        IReadOnlyList<ContentFingerprint> ContentFingerprints,
        IReadOnlyList<GroupRelation> PossibleRelationships,
        IReadOnlyList<ModuleSummary> Modules,
        IReadOnlyList<string> SafetyNotes);

    public interface IReadOnlyDiscoveryModule
    {
        string Name { get; }
        ModuleResult Analyze(GameScope scope,
            MISTScopedCyberpunkDiscovery.ScopedDiscoveryReport inventory,
            CancellationToken cancellationToken);
    }

    public sealed record ModuleResult(IReadOnlyList<EvidenceHint> Hints,
        IReadOnlyList<ContentFingerprint> Fingerprints,
        IReadOnlyList<GroupRelation> Relationships,
        string Note);

    /// <summary>
    /// Bounds file access to relative paths discovered inside this game. Validates
    /// every existing path component against symlinks immediately before reading.
    /// Files changing concurrently can still fail; no snapshot/ownership is claimed.
    /// </summary>
    public sealed class GameScope
    {
        public string Root { get; }

        internal GameScope(string root) => Root = Path.GetFullPath(root);

        public bool TryOpenKnownFile(string relative, out FileStream? stream)
        {
            stream = null;
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return false;
            var normalized = relative.Replace('/', Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(Root, normalized));
            var rel = Path.GetRelativePath(Root, full);
            if (rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(rel)) return false;

            // Do not resolve or traverse linked game or file components.
            var current = Root;
            if (IsLink(current)) return false;
            foreach (var part in rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, part);
                if (IsLink(current)) return false;
            }
            try
            {
                stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (IsLink(full)) { stream.Dispose(); stream = null; return false; }
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static bool IsLink(string path)
        {
            try
            {
                var attributes = File.GetAttributes(path);
                return (attributes & FileAttributes.ReparsePoint) != 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return true; // Fail closed on uncertainty.
            }
        }
    }

    public static Report Scan(string gameRoot, CancellationToken cancellationToken = default)
    {
        var inventory = MISTScopedCyberpunkDiscovery.Scan(gameRoot, cancellationToken);
        var scope = new GameScope(inventory.GameRoot);
        IReadOnlyDiscoveryModule[] modules =
        [
            new InstalledMetadataModule(),
            new SelectedFingerprintModule(),
            new GroupRelationshipModule()
        ];
        var hints = new List<EvidenceHint>();
        var fingerprints = new List<ContentFingerprint>();
        var relations = new List<GroupRelation>();
        var summaries = new List<ModuleSummary>();
        foreach (var module in modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = module.Analyze(scope, inventory, cancellationToken);
                hints.AddRange(result.Hints);
                fingerprints.AddRange(result.Fingerprints);
                relations.AddRange(result.Relationships);
                summaries.Add(new ModuleSummary(module.Name, "completed",
                    result.Hints.Count + result.Fingerprints.Count + result.Relationships.Count, result.Note));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or RegexMatchTimeoutException)
            {
                // Evidence failure never invalidates prior, independent discovery.
                summaries.Add(new ModuleSummary(module.Name, "incomplete", 0, ex.GetType().Name));
            }
        }
        return new Report("mist-game-scoped-discovery/v2b", inventory,
            hints, fingerprints, relations, summaries,
            [
                "Only known files beneath the selected game installation were examined; no Downloads, old managers, or network queries.",
                "A fingerprint identifies file contents, NOT Nexus identity, installation ownership, or mod version.",
                "Embedded version markers and Nexus URLs are unverified local hints, not verified provenance.",
                "Relationships are hypotheses; no groups were merged, files adopted, moved, written, or deleted.",
                "Concurrent filesystem changes may invalidate observations; verify again before any future authorized action."
            ]);
    }

    private sealed class InstalledMetadataModule : IReadOnlyDiscoveryModule
    {
        public string Name => "game-file-metadata";
        private const long MaxFileSize = 1024 * 1024;
        private const int MaxReadBytes = 96 * 1024;
        private const int MaxPerGroup = 8;
        private static readonly Regex VersionRegex = new(
            @"(?im)\b(?:mod[_-]?version|version)\b\s*[:=]\s*[""']?v?(?<value>\d+\.\d+(?:\.\d+){0,3}(?:[-+][a-z0-9_.-]+)?)",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        private static readonly Regex NexusUrlRegex = new(
            @"https?://(?:www\.)?nexusmods\.com/cyberpunk2077/mods/(?<value>\d+)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

        public ModuleResult Analyze(GameScope scope,
            MISTScopedCyberpunkDiscovery.ScopedDiscoveryReport inventory,
            CancellationToken cancellationToken)
        {
            var hints = new List<EvidenceHint>();
            foreach (var group in inventory.CandidateGroups)
            {
                var inspected = 0;
                foreach (var file in group.Files.OrderBy(f => RankMetadata(f.Path)).ThenBy(f => f.Path, StringComparer.Ordinal))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (inspected >= MaxPerGroup) break;
                    if (!IsMetadataCandidate(file) || file.SizeBytes > MaxFileSize) continue;
                    inspected++;
                    if (!scope.TryOpenKnownFile(file.Path, out var stream) || stream is null) continue;
                    using (stream)
                    {
                        if (stream.Length > MaxFileSize) continue;
                        var length = (int)Math.Min(stream.Length, MaxReadBytes);
                        var bytes = new byte[length];
                        var used = stream.ReadAtLeast(bytes, length, throwOnEndOfStream: false);
                        var content = Encoding.UTF8.GetString(bytes, 0, used);
                        foreach (Match match in VersionRegex.Matches(content).Cast<Match>().Take(2))
                        {
                            var value = match.Groups["value"].Value;
                            if (!hints.Any(h => h.GroupKey == group.Key && h.Kind == "embedded-version" && h.Value == value))
                                hints.Add(new EvidenceHint(group.Key, "embedded-version", value, file.Path,
                                    "unverified-local-marker"));
                        }
                        foreach (Match match in NexusUrlRegex.Matches(content).Cast<Match>().Take(3))
                        {
                            var value = match.Groups["value"].Value;
                            if (!hints.Any(h => h.GroupKey == group.Key && h.Kind == "nexus-reference" && h.Value == value))
                                hints.Add(new EvidenceHint(group.Key, "nexus-reference", value, file.Path,
                                    "unverified-could-be-dependency"));
                        }
                    }
                }
            }
            return new ModuleResult(hints, [], [],
                "Bounded inspection of version/readme/manifest text only; skipped logs, databases, and user settings.");
        }

        private static int RankMetadata(string path)
        {
            var name = Path.GetFileName(path).ToLowerInvariant();
            if (name.Contains("version", StringComparison.Ordinal)) return 0;
            if (name.StartsWith("manifest", StringComparison.Ordinal) || name == "info.json") return 1;
            if (name.StartsWith("readme", StringComparison.Ordinal)) return 2;
            if (name == "init.lua") return 3;
            return 4;
        }

        private static bool IsMetadataCandidate(MISTScopedCyberpunkDiscovery.DiscoveredFile file)
        {
            if (file.Role == "possibly-generated") return false;
            var name = Path.GetFileName(file.Path).ToLowerInvariant();
            if (name.Contains("settings", StringComparison.Ordinal) ||
                name.Contains("config", StringComparison.Ordinal) ||
                name.Contains("user", StringComparison.Ordinal) ||
                name.Contains("binding", StringComparison.Ordinal)) return false;
            return name.Contains("version", StringComparison.Ordinal) ||
                   name.StartsWith("readme", StringComparison.Ordinal) ||
                   name.StartsWith("manifest", StringComparison.Ordinal) ||
                   name is "info.json" or "init.lua";
        }
    }

    private sealed class SelectedFingerprintModule : IReadOnlyDiscoveryModule
    {
        public string Name => "selected-file-fingerprints";
        private const long MaxFileSize = 64L * 1024 * 1024;
        public ModuleResult Analyze(GameScope scope,
            MISTScopedCyberpunkDiscovery.ScopedDiscoveryReport inventory,
            CancellationToken cancellationToken)
        {
            var hashes = new List<ContentFingerprint>();
            foreach (var group in inventory.CandidateGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = group.Files
                    .Where(f => f.Role == "package-candidate" && f.SizeBytes <= MaxFileSize && f.SizeBytes > 0)
                    .OrderBy(f => RankFile(f.Path))
                    .ThenBy(f => f.Path, StringComparer.Ordinal)
                    .FirstOrDefault();
                if (file is null || !scope.TryOpenKnownFile(file.Path, out var stream) || stream is null) continue;
                using (stream)
                {
                    if (stream.Length != file.SizeBytes || stream.Length > MaxFileSize) continue;
                    var digest = SHA256.HashData(stream);
                    hashes.Add(new ContentFingerprint(group.Key, file.Path, file.SizeBytes,
                        Convert.ToHexStringLower(digest), "sampled-local-content-only"));
                }
            }
            return new ModuleResult([], hashes, [],
                "At most one bounded package-file SHA-256 per group, useful for later exact-release comparisons.");
        }
        private static int RankFile(string path)
        {
            var name = path.ToLowerInvariant();
            if (name.EndsWith(".dll") || name.EndsWith(".asi")) return 0;
            if (name.EndsWith(".archive")) return 1;
            if (name.EndsWith("version.reds")) return 2;
            if (name.EndsWith("init.lua")) return 3;
            return 4;
        }
    }

    private sealed class GroupRelationshipModule : IReadOnlyDiscoveryModule
    {
        public string Name => "component-relationship-hints";
        public ModuleResult Analyze(GameScope scope,
            MISTScopedCyberpunkDiscovery.ScopedDiscoveryReport inventory,
            CancellationToken cancellationToken)
        {
            var relations = new List<GroupRelation>();
            var groups = inventory.CandidateGroups.OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
            for (var i = 0; i < groups.Length; i++)
            for (var j = i + 1; j < groups.Length; j++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var a = groups[i].Key;
                var b = groups[j].Key;
                var shorter = a.Length <= b.Length ? a : b;
                var longer = a.Length <= b.Length ? b : a;
                var strong = shorter.Length >= 5 && longer.StartsWith(shorter, StringComparison.Ordinal) &&
                             longer.Length - shorter.Length >= 3;
                var specialized = shorter == "2bt" && longer.StartsWith("2bt", StringComparison.Ordinal);
                if (!strong && !specialized) continue;
                relations.Add(new GroupRelation(a, b, "possible-component-or-related-mod",
                    "Shared name prefix only; could be addon, compatibility patch, optional component or unrelated mod. Not merged."));
            }
            return new ModuleResult([], [], relations, "Relationships are hints and never consolidate management ownership.");
        }
    }
}
