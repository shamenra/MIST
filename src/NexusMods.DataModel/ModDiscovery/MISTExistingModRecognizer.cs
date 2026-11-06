using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NexusMods.DataModel.ModDiscovery;

/// <summary>
/// MIST-006-14B1: read-only Cyberpunk file/archive recognition.
/// Does not create loadout items, write to the MIST database, or mutate game files.
/// The ZIP sidecar is unverified metadata, NOT confirmation from Nexus Mods.
/// </summary>
public static class MISTExistingModRecognizer
{
    private const long MaxFileBytes = 1024L * 1024 * 1024; // Limit costly / malicious archive entries.
    private const int MaxArchives = 2500;
    private const int MaxInstalledFiles = 50000;
    private static readonly string[] Roots =
    [
        "archive/pc/mod/", "archive/pc/ep1/mod/", "r6/scripts/", "r6/tweaks/",
        "r6/config/", "red4ext/plugins/", "bin/x64/plugins/", "mods/",
        "engine/config/platform/pc/"
    ];

    public sealed record FileMatch(string Path, string State, string? InstalledSha256, string? ArchiveSha256);

    public sealed record NexusHint(string Source, string? Game, long? ModId, long? FileId, string? Version);

    public sealed record ArchiveMatch(
        string ArchivePath, long ArchiveSize, string? ArchiveMd5,
        string State, string IdentityState, NexusHint? NexusHint,
        int ExactFiles, int DifferentFiles, int MissingFiles,
        IReadOnlyList<FileMatch> Files, IReadOnlyList<string> Warnings);

    public sealed record DiscoveryReport(
        string Schema, string Game, string GameRoot, string DownloadsRoot,
        int InstalledFileCount, int ArchivesInspected,
        IReadOnlyList<ArchiveMatch> Archives,
        IReadOnlyList<string> UnknownInstalledFiles,
        IReadOnlyList<string> SkippedArchives,
        IReadOnlyList<string> SafetyNotes);

    /// <summary>
    /// Known Cyberpunk mod destinations only. Paths outside these locations are not scanned.
    /// Discovery is filesystem-read-only, except for the caller's explicit report output.
    /// </summary>
    public static async Task<DiscoveryReport> ScanCyberpunkAsync(
        string gameRoot, string downloadsRoot, CancellationToken cancellationToken = default)
    {
        gameRoot = Path.GetFullPath(gameRoot);
        downloadsRoot = Path.GetFullPath(downloadsRoot);
        if (!Directory.Exists(gameRoot)) throw new DirectoryNotFoundException($"Game directory missing: {gameRoot}");
        if (!Directory.Exists(downloadsRoot)) throw new DirectoryNotFoundException($"Downloads directory missing: {downloadsRoot}");
        if (IsWithin(gameRoot, downloadsRoot) || IsWithin(downloadsRoot, gameRoot))
            throw new ArgumentException("The game directory and archive directory must not contain one another.");

        // Never follow symlinks during enumeration or scan outside explicit roots.
        var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in Roots)
        {
            var directory = Path.Combine(gameRoot, root.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(directory) || IsSymbolicLink(directory)) continue;
            foreach (var file in EnumerateFilesSafe(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(gameRoot, file).Replace('\\', '/');
                if (!installed.ContainsKey(relative)) installed.Add(relative, file);
                if (installed.Count > MaxInstalledFiles)
                    throw new InvalidOperationException("Too many installed files for a bounded read-only scan.");
            }
        }

        var archiveFiles = EnumerateFilesSafe(downloadsRoot)
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .Take(MaxArchives + 1).ToArray();
        if (archiveFiles.Length > MaxArchives)
            throw new InvalidOperationException("Archive count exceeds the read-only scan limit.");

        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = new List<ArchiveMatch>();
        var skipped = new List<string>();

        foreach (var archivePath in archiveFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
                var files = new List<FileMatch>();
                var warnings = new List<string>();
                var seenArchiveDestinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in zip.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var dest = TryGetCyberpunkDestination(entry.FullName);
                    if (dest is null || entry.Name.Length == 0) continue;
                    if (!seenArchiveDestinations.Add(dest))
                    {
                        warnings.Add($"Multiple archive entries target {dest}; installer choices cannot be inferred.");
                        continue;
                    }

                    if (entry.Length > MaxFileBytes ||
                        (entry.CompressedLength > 0 && entry.Length / Math.Max(entry.CompressedLength, 1) > 200))
                    {
                        warnings.Add($"Skipped oversized/high-expansion entry: {dest}");
                        continue;
                    }

                    if (!installed.TryGetValue(dest, out var diskPath))
                    {
                        files.Add(new FileMatch(dest, "missing", null, null));
                        continue;
                    }

                    if (new FileInfo(diskPath).Length > MaxFileBytes)
                    {
                        warnings.Add($"Skipped oversized installed file: {dest}");
                        continue;
                    }

                    if (!hashes.TryGetValue(dest, out var diskHash))
                    {
                        await using var diskStream = new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        diskHash = Convert.ToHexString(await SHA256.HashDataAsync(diskStream, cancellationToken));
                        hashes[dest] = diskHash;
                    }

                    await using var entryStream = entry.Open();
                    var archiveHash = Convert.ToHexString(await SHA256.HashDataAsync(entryStream, cancellationToken));
                    bool same = string.Equals(diskHash, archiveHash, StringComparison.OrdinalIgnoreCase);
                    files.Add(new FileMatch(dest, same ? "exact" : "different", diskHash, archiveHash));
                    if (same) claimed.Add(dest);
                }

                if (files.Count == 0 && warnings.Count == 0) continue;
                var exact = files.Count(f => f.State == "exact");
                var different = files.Count(f => f.State == "different");
                var missing = files.Count(f => f.State == "missing");
                string state = warnings.Count > 0 ? "requires-review" :
                    exact > 0 && different == 0 && missing == 0 ? "exact-archive-content" :
                    exact > 0 ? "partial-match" :
                    different > 0 ? "path-conflict-only" : "not-installed";
                var hint = ReadNexusSidecar(archivePath);
                // MD5 identifies the original download via Nexus' archive hash lookup;
                // SHA-256 remains the content-equality check for extracted files.
                string? archiveMd5 = null;
                if (exact > 0 && stream.Length <= 2L * 1024 * 1024 * 1024)
                {
                    stream.Position = 0;
                    archiveMd5 = Convert.ToHexString(await MD5.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
                }
                string identity = hint is not null && exact > 0
                    ? "sidecar-claimed-nexus-unverified"
                    : "unknown-nexus-origin";
                matches.Add(new ArchiveMatch(archivePath, stream.Length, archiveMd5,
                    state, identity, hint, exact, different, missing, files, warnings));
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                skipped.Add($"{archivePath}: {ex.GetType().Name}");
            }
        }

        return new DiscoveryReport(
            "mist-existing-mod-discovery/v1", "Cyberpunk 2077", gameRoot, downloadsRoot,
            installed.Count, archiveFiles.Length, matches,
            installed.Keys.Where(path => !claimed.Contains(path)).Order(StringComparer.OrdinalIgnoreCase).ToArray(),
            skipped,
            [
                "Read-only: no game, download, loadout or database files were changed.",
                "Exact means SHA-256 equality between a local ZIP entry and an installed game file; it does not prove Nexus provenance.",
                "Nexus IDs from local sidecar JSON are unverified hints until validated against an authenticated source.",
                "Partial matches and duplicate target paths are NOT safe for automatic adoption.",
                "Only ZIPs and known Cyberpunk mod directories are inspected; 7z/RAR and other locations require later adapters."
            ]);
    }

    public static string? TryGetCyberpunkDestination(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName)) return null;
        var path = entryName.Replace('\\', '/');
        if (path.StartsWith("/", StringComparison.Ordinal) || path.Contains(':') ||
            path.Split('/').Any(part => part is "." or ".." or "")) return null;
        int index = int.MaxValue;
        foreach (var root in Roots)
        {
            int found = path.IndexOf(root, StringComparison.OrdinalIgnoreCase);
            if (found >= 0 && (found == 0 || path[found - 1] == '/') && found < index) index = found;
        }
        if (index == int.MaxValue) return null;
        return path[index..];
    }

    private static NexusHint? ReadNexusSidecar(string zipPath)
    {
        foreach (var sidecar in new[] { zipPath + ".meta", Path.ChangeExtension(zipPath, ".meta") }.Distinct())
        {
            if (!File.Exists(sidecar) || IsSymbolicLink(sidecar) || new FileInfo(sidecar).Length > 1024 * 1024) continue;
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(sidecar));
                if (json.RootElement.ValueKind != JsonValueKind.Object) continue;
                var root = json.RootElement;
                var source = GetString(root, "source");
                var game = GetString(root, "game") ?? GetString(root, "gameId");
                var modId = GetLong(root, "modId");
                var fileId = GetLong(root, "fileId");
                var version = GetString(root, "version");
                if (modId is null || modId < 1 ||
                    !string.Equals(source, "nexus", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(source, "nexusmods", StringComparison.OrdinalIgnoreCase)) continue;
                if (game is not null && !game.Equals("cyberpunk2077", StringComparison.OrdinalIgnoreCase) &&
                    !game.Equals("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase)) continue;
                return new NexusHint(source!, game, modId, fileId, version);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Sidecars are merely hints: malformed metadata must not fail the scan.
            }
        }
        return null;
    }

    private static string? GetString(JsonElement root, string key)
    {
        foreach (var property in root.EnumerateObject())
            if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.ToString();
        return null;
    }

    private static long? GetLong(JsonElement root, string key)
    {
        var value = GetString(root, key);
        return long.TryParse(value, out var n) ? n : null;
    }

    private static bool IsSymbolicLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (IOException) { return true; }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var queue = new Queue<string>();
        queue.Enqueue(root);
        while (queue.TryDequeue(out var dir))
        {
            if (IsSymbolicLink(dir)) continue;
            IEnumerable<string> children;
            try { children = Directory.EnumerateFileSystemEntries(dir).ToArray(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            foreach (var child in children)
            {
                if (IsSymbolicLink(child)) continue;
                if (Directory.Exists(child)) queue.Enqueue(child);
                else if (File.Exists(child)) yield return child;
            }
        }
    }

    public static bool IsWithin(string child, string parent)
    {
        child = Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar);
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        return child.Equals(parent, StringComparison.Ordinal) ||
               child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
