using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Expected original-file metadata obtained from a local Steam depot manifest.
/// A manifest SHA-1 is evidence about what Steam intended to install; it is
/// NOT a measurement of the installed file, a public game version, or a
/// filesystem mutation authorization.
/// </summary>
public sealed record MISTSteamManifestFile(string Path, long Size, string Sha1);

public sealed record MISTSteamDepotCatalog(uint DepotId, ulong ManifestId,
    IReadOnlyList<MISTSteamManifestFile> Files);

/// <summary>
/// A sidecar snapshot of the Steam manifests for ONE installation and build.
/// MissingDepots is nonempty when the locally cached manifests were incomplete.
/// Do not use an incomplete snapshot as the game's original-file baseline.
/// </summary>
public sealed record MISTSteamLocalManifestCatalog(
    uint AppId, ulong BuildId, string BuildSignature, string InstallationPathHash,
    DateTimeOffset CapturedUtc, IReadOnlyList<MISTSteamDepotCatalog> Depots,
    IReadOnlyList<uint> MissingDepots)
{
    public bool Complete => Depots.Count > 0 && MissingDepots.Count == 0;
}

public static class MISTSteamLocalManifestRules
{
    /// <summary>
    /// Rejects absolute paths, traversal, unsafe Windows path segments, and
    /// malformed manifest file names. Steam depot paths are forward-slash
    /// relative to the game installation root.
    /// </summary>
    public static bool TryNormalizeRelativeFile(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 4096) return false;
        var name = raw.Replace('\\', '/');
        if (name.StartsWith('/') || name.EndsWith('/') || name.Contains(':') ||
            name.Any(char.IsControl)) return false;
        var parts = name.Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." ||
                           p.EndsWith(' ') || p.EndsWith('.'))) return false;
        normalized = name;
        return true;
    }

    public static bool ValidSha1(string? sha1) =>
        sha1 is { Length: 40 } && sha1.All(c =>
            c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    /// <summary>
    /// Distinguishes identical builds installed in separate directories,
    /// without using the installation path directly as a filename.
    /// </summary>
    public static string InstallationPathHash(string gameFolder)
    {
        var full = System.IO.Path.GetFullPath(gameFolder)
            .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) full = full.ToUpperInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)));
    }

    public static string CatalogFilePath(string catalogRoot, MISTSteamBuildIdentity identity,
        string gameFolder) => System.IO.Path.Combine(
            System.IO.Path.GetFullPath(catalogRoot),
            identity.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            identity.Signature,
            InstallationPathHash(gameFolder) + ".json");

    /// <summary>
    /// Validates the whole sidecar against the currently detected Steam
    /// identity. A partial snapshot remains informational and may be retried.
    /// </summary>
    public static bool ValidCatalog(MISTSteamLocalManifestCatalog? catalog,
        MISTSteamBuildIdentity identity, string gameFolder)
    {
        if (catalog is null || catalog.AppId != identity.AppId ||
            catalog.BuildId != identity.BuildId ||
            catalog.BuildSignature != identity.Signature ||
            catalog.InstallationPathHash != InstallationPathHash(gameFolder) ||
            catalog.Depots is null || catalog.MissingDepots is null) return false;

        var declaredDepots = new HashSet<uint>();
        foreach (var depot in catalog.Depots)
        {
            if (!identity.InstalledDepots.TryGetValue(depot.DepotId, out var manifestId) ||
                depot.ManifestId != manifestId || depot.Files is null || depot.Files.Count == 0 ||
                !declaredDepots.Add(depot.DepotId)) return false;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in depot.Files)
            {
                if (!TryNormalizeRelativeFile(file.Path, out var clean) || clean != file.Path ||
                    file.Size < 0 || !ValidSha1(file.Sha1) || !paths.Add(clean)) return false;
            }
        }
        foreach (var missing in catalog.MissingDepots)
        {
            if (!identity.InstalledDepots.ContainsKey(missing) ||
                !declaredDepots.Add(missing)) return false;
        }
        return declaredDepots.Count == identity.InstalledDepots.Count;
    }

    /// <summary>
    /// Optional full-file check against the expected SHA-1 from Steam's local
    /// manifest. Does not alter the installed file and rejects symlinks.
    /// This is intentionally separate from automatic metadata capture: it may
    /// require reading many gigabytes for a large game.
    /// </summary>
    public static async Task<bool> MatchesInstalledFileAsync(
        string gameFolder, MISTSteamManifestFile record,
        CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeRelativeFile(record.Path, out var safe) ||
            !ValidSha1(record.Sha1) || record.Size < 0) return false;
        var root = System.IO.Path.GetFullPath(gameFolder);
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(root,
            safe.Replace('/', System.IO.Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root.TrimEnd(System.IO.Path.DirectorySeparatorChar) +
            System.IO.Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, comparison)) return false;
        try
        {
            // Refuse symlinks anywhere in the path; lexical containment is
            // not sufficient when symlinks can lead outside the game root.
            for (var cursor = new FileInfo(full).Directory;
                 cursor is not null && cursor.FullName.StartsWith(prefix, comparison);
                 cursor = cursor.Parent)
            {
                if ((cursor.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            }
            var info = new FileInfo(full);
            if (!info.Exists || info.Length != record.Size ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            await using var stream = new FileStream(full, FileMode.Open, FileAccess.Read,
                FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sha = SHA1.Create();
            var bytes = await sha.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(bytes).Equals(record.Sha1, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
