using System.Text.Json;
using NexusMods.Abstractions.Games.FileHashes;
using SteamKit2;

namespace NexusMods.Games.FileHashes;

/// <summary>
/// Captures local Steam depot manifest metadata to a separate, append-by-build
/// M.I.S.T. catalog. The catalog is NOT a Nexus hash DB and is NOT used by
/// deletion authority or public-version compatibility until a verified
/// resolver adapter is added. Neither Steam nor game files are written.
/// </summary>
public static class MISTSteamDepotManifestImporter
{
    private const long MaxCacheFileBytes = 128L * 1024 * 1024;
    private const int MaxFilesPerDepot = 300_000;

    public static MISTSteamLocalManifestCatalog? Capture(
        string gameFolder, string catalogRoot, CancellationToken cancellationToken = default)
    {
        if (!MISTSteamBuildIdentityReader.TryReadForGameFolder(gameFolder, out var identity) ||
            identity is null) return null;

        var output = MISTSteamLocalManifestRules.CatalogFilePath(catalogRoot, identity, gameFolder);
        if (File.Exists(output))
        {
            try
            {
                var previous = JsonSerializer.Deserialize<MISTSteamLocalManifestCatalog>(
                    File.ReadAllText(output));
                if (MISTSteamLocalManifestRules.ValidCatalog(previous, identity, gameFolder) &&
                    previous!.Complete)
                    return previous;
            }
            catch (IOException) { /* Retest cached manifests. */ }
            catch (JsonException) { /* Retest cached manifests. */ }
        }

        var depots = new List<MISTSteamDepotCatalog>();
        var missing = new List<uint>();
        foreach (var pair in identity.InstalledDepots.OrderBy(pair => pair.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryReadCachedDepot(identity.AppManifestPath, pair.Key, pair.Value,
                    out var depot))
                missing.Add(pair.Key);
            else
                depots.Add(depot!);
        }

        var snapshot = new MISTSteamLocalManifestCatalog(identity.AppId, identity.BuildId,
            identity.Signature, MISTSteamLocalManifestRules.InstallationPathHash(gameFolder),
            DateTimeOffset.UtcNow, depots, missing);
        if (!MISTSteamLocalManifestRules.ValidCatalog(snapshot, identity, gameFolder))
            return null;

        // Application-owned catalog only. Preserve records for other builds
        // and other installation paths; write atomically to this one key.
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return snapshot;
    }

    private static bool TryReadCachedDepot(string appManifestPath, uint depotId,
        ulong manifestId, out MISTSteamDepotCatalog? result)
    {
        result = null;
        foreach (var root in CandidateCacheRoots(appManifestPath))
        {
            var cacheFile = Path.Combine(root, $"{depotId}_{manifestId}.manifest");
            try
            {
                var fileInfo = new FileInfo(cacheFile);
                if (!fileInfo.Exists || fileInfo.Length is <= 0 or > MaxCacheFileBytes ||
                    (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var manifest = DepotManifest.LoadFromFile(cacheFile);
                if (manifest is null || manifest.DepotID != depotId ||
                    manifest.ManifestGID != manifestId || manifest.FilenamesEncrypted ||
                    manifest.Files is null) continue;

                var files = new List<MISTSteamManifestFile>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in manifest.Files)
                {
                    // Same file selection used by our proven Cyberpunk
                    // SteamManifestExporter: regular files with chunk data.
                    if (item.Chunks.Count == 0) continue;
                    if (!MISTSteamLocalManifestRules.TryNormalizeRelativeFile(
                            item.FileName, out var clean) || !seen.Add(clean) ||
                        item.TotalSize > long.MaxValue ||
                        item.FileHash is not { Length: 20 })
                        return false;
                    files.Add(new MISTSteamManifestFile(clean,
                        checked((long)item.TotalSize), Convert.ToHexString(item.FileHash)));
                    if (files.Count > MaxFilesPerDepot) return false;
                }
                if (files.Count == 0) continue;
                result = new MISTSteamDepotCatalog(depotId, manifestId, files);
                return true;
            }
            catch (IOException) { /* Another cache location may contain it. */ }
            catch (UnauthorizedAccessException) { }
            catch (InvalidDataException) { }
            catch (ArgumentException) { }
        }
        return false;
    }

    private static IEnumerable<string> CandidateCacheRoots(string appManifestPath)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var steamapps = Path.GetDirectoryName(appManifestPath)!;
        var library = Directory.GetParent(steamapps)?.FullName ?? steamapps;
        var roots = new[]
        {
            Path.Combine(library, "depotcache"),
            Path.Combine(home, ".var/app/com.valvesoftware.Steam/.local/share/Steam/depotcache"),
            Path.Combine(home, ".local/share/Steam/depotcache"),
            Path.Combine(home, ".steam/steam/depotcache"),
        };
        return roots.Distinct(StringComparer.Ordinal);
    }
}
