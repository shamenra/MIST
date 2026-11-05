using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// A read-only, installation-scoped bridge from Steam's expected SHA-1 manifest
/// files to verified original-file evidence. Catalog metadata alone is NEVER
/// evidence that installed bytes are unchanged. This does not grant deletion
/// authority or assert a public game version.
/// </summary>
public static class MISTSteamVerifiedOriginalCatalog
{
    private const long MaxCatalogBytes = 64L * 1024 * 1024;

    /// <summary>
    /// Only load complete catalog records for the exact current installation,
    /// build signature, and depot manifest set. A moved/copied game directory,
    /// changed appmanifest, missing depot, or malformed JSON fails closed.
    /// </summary>
    public static bool TryLoadComplete(string gameFolder, string catalogRoot,
        out MISTSteamBuildIdentity? identity,
        out MISTSteamLocalManifestCatalog? catalog)
    {
        identity = null;
        catalog = null;
        if (!MISTSteamBuildIdentityReader.TryReadForGameFolder(gameFolder,
                out var current) || current is null)
            return false;

        var path = MISTSteamLocalManifestRules.CatalogFilePath(
            catalogRoot, current, gameFolder);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length is <= 0 or > MaxCatalogBytes ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
                return false;

            var saved = JsonSerializer.Deserialize<MISTSteamLocalManifestCatalog>(
                File.ReadAllText(path));
            if (!MISTSteamLocalManifestRules.ValidCatalog(saved, current, gameFolder) ||
                !saved!.Complete)
                return false;

            identity = current;
            catalog = saved;
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
    }

    /// <summary>
    /// An original-file candidate must have exactly one consistent expected
    /// identity across all depots. Conflicting cross-depot entries are rejected.
    /// This only identifies what Steam EXPECTED, not what is on disk.
    /// </summary>
    public static bool TryFindExpectedFile(MISTSteamLocalManifestCatalog catalog,
        MISTSteamBuildIdentity identity, string gameFolder, string relativePath,
        out MISTSteamManifestFile? expected)
    {
        expected = null;
        if (!MISTSteamLocalManifestRules.ValidCatalog(catalog, identity, gameFolder) ||
            !catalog.Complete ||
            !MISTSteamLocalManifestRules.TryNormalizeRelativeFile(relativePath,
                out var normalized) || normalized != relativePath)
            return false;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var file in catalog.Depots.SelectMany(depot => depot.Files))
        {
            if (!file.Path.Equals(normalized, comparison)) continue;
            if (expected is null)
            {
                expected = file;
                continue;
            }
            if (expected.Size != file.Size ||
                !expected.Sha1.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                expected = null;
                return false;
            }
        }
        return expected is not null;
    }

    /// <summary>
    /// Return the expected file only when the installed bytes actually match
    /// Steam's SHA-1 and size. This may read a large file and must be called
    /// on demand, never for every game file during UI startup.
    /// </summary>
    public static async Task<MISTSteamManifestFile?> VerifyInstalledFileAsync(
        string gameFolder, MISTSteamLocalManifestCatalog catalog,
        MISTSteamBuildIdentity identity, string relativePath,
        CancellationToken cancellationToken = default)
    {
        if (!TryFindExpectedFile(catalog, identity, gameFolder,
                relativePath, out var expected))
            return null;
        return await MISTSteamLocalManifestRules.MatchesInstalledFileAsync(
            gameFolder, expected!, cancellationToken) ? expected : null;
    }
}
