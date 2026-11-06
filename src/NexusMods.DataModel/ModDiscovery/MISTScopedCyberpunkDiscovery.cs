using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NexusMods.DataModel.ModDiscovery;

/// <summary>
/// MIST-006-14B2A: bounded, manager-independent, read-only installed-content grouping.
/// Only reads paths inside the explicitly selected Cyberpunk installation. No archive,
/// API, prior-manager, MIST database, or whole-system search occurs here.
/// Structural groups are hypotheses, NOT verified mod identities or file ownership.
/// </summary>
public static class MISTScopedCyberpunkDiscovery
{
    private const int MaxFiles = 75000;
    private const int MaxDepth = 32;

    // Restrict discovery to known mod-capable destinations. Deliberately NOT scanning
    // the entire game or all r6/config (which contains ordinary game-owned files).
    private static readonly string[] DirectoryRoots =
    [
        "archive/pc/mod", "archive/pc/ep1/mod", "r6/scripts", "r6/tweaks",
        "r6/config/redsUserHints", "r6/config/cybercmd", "red4ext/plugins",
        "bin/x64/plugins/cyber_engine_tweaks", "bin/x64/plugins/address_library",
        "mods"
    ];

    private static readonly string[] FileRoots =
    [
        "bin/x64/plugins/cyber_engine_tweaks.asi"
    ];

    public sealed record DiscoveredFile(string Path, long SizeBytes, string Role, string Evidence);
    public sealed record ModGroup(string Key, string SuggestedName, string Confidence,
        IReadOnlyList<string> Evidence, IReadOnlyList<DiscoveredFile> Files);
    public sealed record ScopedDiscoveryReport(string Schema, string Game, string GameRoot,
        int InstalledFileCount, IReadOnlyList<ModGroup> CandidateGroups,
        IReadOnlyList<DiscoveredFile> UnattributedFiles,
        IReadOnlyList<string> SkippedPaths, IReadOnlyList<string> SafetyNotes);

    /// <summary>Does not write anything, follow directory links, or read unrelated directories.</summary>
    public static ScopedDiscoveryReport Scan(string gameRoot, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(gameRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Game directory missing: {root}");
        if (IsLink(root)) throw new InvalidOperationException("Game root itself is a symlink. Select the physical installation directory.");

        var found = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<string>();
        foreach (var relativeRoot in DirectoryRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TrySafePath(root, relativeRoot, out var directory))
            {
                skipped.Add(relativeRoot + " (linked or invalid ancestor)");
                continue;
            }
            if (!Directory.Exists(directory)) continue;
            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((directory, 0));
            while (queue.TryDequeue(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Depth > MaxDepth) { skipped.Add(ToRelative(root, item.Path) + " (depth limit)"); continue; }
                if (IsLink(item.Path)) { skipped.Add(ToRelative(root, item.Path) + " (symlink)"); continue; }

                string[] children;
                try { children = Directory.GetFileSystemEntries(item.Path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(ToRelative(root, item.Path) + " (unreadable)");
                    continue;
                }
                Array.Sort(children, StringComparer.Ordinal);
                foreach (var child in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (IsLink(child)) { skipped.Add(ToRelative(root, child) + " (symlink)"); continue; }
                    if (Directory.Exists(child)) { queue.Enqueue((child, item.Depth + 1)); continue; }
                    if (!File.Exists(child)) continue;
                    var relative = ToRelative(root, child);
                    if (relative.StartsWith("../", StringComparison.Ordinal) || relative == "..")
                        throw new InvalidOperationException("Discovery escaped the selected game directory.");
                    found.TryAdd(relative, child);
                    if (found.Count > MaxFiles)
                        throw new InvalidOperationException("Game mod-path file count exceeds safe scan limit.");
                }
            }
        }

        foreach (var relative in FileRoots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TrySafePath(root, relative, out var full)) { skipped.Add(relative + " (linked or invalid ancestor)"); continue; }
            if (File.Exists(full) && !IsLink(full)) found.TryAdd(relative, full);
        }
        if (found.Count > MaxFiles)
            throw new InvalidOperationException("Game mod-path file count exceeds safe scan limit.");

        var grouped = new SortedDictionary<string, List<DiscoveredFile>>(StringComparer.Ordinal);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var evidence = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var unknown = new List<DiscoveredFile>();

        foreach (var (relative, full) in found)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long length;
            try { length = new FileInfo(full).Length; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { skipped.Add(relative + " (unreadable metadata)"); continue; }

            var candidate = FindAnchor(relative);
            var role = ClassifyRole(relative);
            var item = new DiscoveredFile(relative, length, role,
                candidate is null ? "recognized game-relative location only" : candidate.Value.Evidence);
            if (candidate is null) { unknown.Add(item); continue; }
            var (name, proof) = candidate.Value;
            var key = Normalize(name);
            if (key.Length < 3 || GenericNames.Contains(key)) { unknown.Add(item); continue; }
            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped.Add(key, list);
                names[key] = name;
                evidence[key] = new SortedSet<string>(StringComparer.Ordinal);
            }
            list.Add(item);
            evidence[key].Add(proof);
        }

        var groups = grouped.Select(pair => new ModGroup(pair.Key, names[pair.Key],
            pair.Value.Count >= 2 ? "structural-candidate" : "weak-structural-candidate",
            [.. evidence[pair.Key], "Same normalized folder/stem is only a grouping hypothesis; Nexus identity and ownership are UNVERIFIED."],
            pair.Value.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray())).ToArray();

        return new ScopedDiscoveryReport("mist-game-scoped-discovery/v2a", "Cyberpunk 2077", root,
            found.Count, groups, unknown, skipped,
            [
                "Strictly game-scoped: no Downloads, other game, Vortex, Nexus, or filesystem-wide search.",
                "Skipped linked directories/files and linked intermediate path components.",
                "This is a structural inventory only; it does NOT establish Nexus identity, version, or management permission.",
                "Mutable/generated classifications are conservative hints; every user file remains untouched.",
                "No game files, library records, loadouts or database state were modified."
            ]);
    }

    /// <summary>Report writes must be outside the game and must not traverse linked parents.</summary>
    public static bool IsSafeReportPath(string output, string gameRoot)
    {
        var fullOutput = Path.GetFullPath(output);
        var root = Path.GetFullPath(gameRoot);
        if (MISTExistingModRecognizer.IsWithin(fullOutput, root)) return false;
        // Refuse linked ancestor paths for the report to avoid redirecting the write
        // through an external symlink into the game directory.
        DirectoryInfo? parent = Directory.GetParent(fullOutput);
        while (parent is not null)
        {
            if (IsLink(parent.FullName)) return false;
            parent = parent.Parent;
        }
        return true;
    }

    private static readonly HashSet<string> GenericNames = new(StringComparer.Ordinal)
    {
        "mod", "mods", "script", "scripts", "init", "config", "settings", "version",
        "readme", "license", "module", "example", "default", "plugin", "plugins",
        "main", "index", "info", "data", "user", "test", "base"
    };

    private static (string Name, string Evidence)? FindAnchor(string relative)
    {
        var p = relative.Replace('\\', '/');
        var lower = p.ToLowerInvariant();
        if (lower == "bin/x64/plugins/cyber_engine_tweaks.asi")
            return ("Cyber Engine Tweaks", "known Cyber Engine Tweaks loader path");
        if (lower.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/", StringComparison.Ordinal))
            return FolderAfter(p, "bin/x64/plugins/cyber_engine_tweaks/mods/", "CET mod folder");
        if (lower.StartsWith("bin/x64/plugins/cyber_engine_tweaks/", StringComparison.Ordinal))
            return ("Cyber Engine Tweaks", "CET framework directory");
        if (lower.StartsWith("bin/x64/plugins/address_library/", StringComparison.Ordinal))
            return ("Address Library", "address library directory");
        if (lower.StartsWith("red4ext/plugins/", StringComparison.Ordinal))
            return FolderAfter(p, "red4ext/plugins/", "RED4ext plugin folder");
        if (lower.StartsWith("r6/scripts/", StringComparison.Ordinal))
            return FolderOrStem(p, "r6/scripts/", "REDscript folder", "REDscript file stem");
        if (lower.StartsWith("r6/tweaks/", StringComparison.Ordinal))
            return FolderOrStem(p, "r6/tweaks/", "TweakXL folder", "TweakXL file stem");
        if (lower.StartsWith("r6/config/redsuserhints/", StringComparison.Ordinal))
            return StemAfter(p, "r6/config/redsUserHints/", "REDscript user hint stem");
        if (lower.StartsWith("archive/pc/mod/", StringComparison.Ordinal))
            return StemAfter(p, "archive/pc/mod/", "archive mod filename stem");
        if (lower.StartsWith("archive/pc/ep1/mod/", StringComparison.Ordinal))
            return StemAfter(p, "archive/pc/ep1/mod/", "expansion archive filename stem");
        if (lower.StartsWith("mods/", StringComparison.Ordinal))
            return FolderAfter(p, "mods/", "REDmod folder");
        // cybercmd's shared scc.toml is configuration, not proof of a particular mod.
        return null;
    }

    private static (string Name, string Evidence)? FolderAfter(string path, string prefix, string label)
    {
        var remaining = path[prefix.Length..];
        var separator = remaining.IndexOf('/');
        return separator > 0 ? (remaining[..separator], label) : null;
    }

    private static (string Name, string Evidence)? FolderOrStem(string path, string prefix, string folderLabel, string stemLabel)
    {
        var remaining = path[prefix.Length..];
        var separator = remaining.IndexOf('/');
        if (separator > 0) return (remaining[..separator], folderLabel);
        return !remaining.Contains('/') ? (WithoutModExtensions(remaining), stemLabel) : null;
    }

    private static (string Name, string Evidence)? StemAfter(string path, string prefix, string label)
    {
        var remaining = path[prefix.Length..];
        return remaining.Contains('/') ? null : (WithoutModExtensions(remaining), label);
    }

    private static string WithoutModExtensions(string name)
    {
        if (name.EndsWith(".archive.xl", StringComparison.OrdinalIgnoreCase)) return name[..^11];
        if (name.EndsWith(".archive", StringComparison.OrdinalIgnoreCase)) return name[..^8];
        return Path.GetFileNameWithoutExtension(name);
    }

    private static string Normalize(string name)
    {
        var builder = new StringBuilder();
        foreach (var ch in name) if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        return builder.ToString();
    }

    private static string ClassifyRole(string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".log") || lower.EndsWith(".log?") ||
            lower.EndsWith(".sqlite3") || lower.EndsWith(".tmp") || lower.EndsWith(".cache"))
            return "possibly-generated";
        if (lower.EndsWith(".ini") || lower.EndsWith(".json") ||
            lower.EndsWith(".toml") || lower.EndsWith(".yaml") || lower.EndsWith(".yml") ||
            lower.EndsWith(".xml")) return "potentially-mutable";
        return "package-candidate";
    }

    private static bool TrySafePath(string root, string relative, out string full)
    {
        full = root;
        foreach (var component in relative.Replace('\\', '/').Split('/'))
        {
            if (component.Length == 0 || component is "." or "..") return false;
            full = Path.Combine(full, component);
            if (IsLink(full)) return false;
        }
        return MISTExistingModRecognizer.IsWithin(full, root);
    }

    private static string ToRelative(string root, string full) => Path.GetRelativePath(root, full).Replace('\\', '/');

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return true; }
    }
}
