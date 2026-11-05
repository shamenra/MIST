using DynamicData.Kernel;
using JetBrains.Annotations;
using NexusMods.Abstractions.Games.FileHashes.Models;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Sdk;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.NexusModsApi;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Interface for the file hashes service, which provides a way to download and update the file hashes database
/// </summary>
[PublicAPI]
public interface IFileHashesService
{
    /// <summary>
    /// The current file hashes database, will throw an error if not initialized via GetFileHashesDb first.
    /// </summary>
    public IDb Current { get; }

    /// <summary>
    /// Get the file hashes database, downloading it if necessary
    /// </summary>
    public ValueTask<IDb> GetFileHashesDb();

    /// <summary>
    /// Force an update of the file hashes database
    /// </summary>
    public Task CheckForUpdate(bool forceUpdate = false);

    /// <summary>
    /// M.I.S.T.: On detection, collect AVAILABLE local Steam depot metadata
    /// into an application-owned sidecar catalog. No file mutation authority,
    /// public version mapping, or original-file verification is inferred.
    /// Other store and test implementations may opt out.
    /// </summary>
    public Task MISTCaptureInstalledSteamManifestsAsync(
        string gameFolder, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// M.I.S.T.: On-demand installation-aware original-file verification.
    /// Converts a complete local Steam manifest catalog entry to a normal
    /// GameFileRecord ONLY when the installed file matches Steam's SHA-1.
    /// Unlike GetGameFiles(store, locatorIds), this cannot lose installation
    /// identity. Never use this evidence to authorize a file deletion.
    /// A missing/changed file yields None, not a presumed original.
    /// </summary>
    public ValueTask<Optional<GameFileRecord>> MISTFindVerifiedOriginalSteamFileAsync(
        GameInstallation installation, GamePath path,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(Optional<GameFileRecord>.None);

    /// <summary>
    /// Gets all known vanity versions for a given game.
    /// </summary>
    public IEnumerable<VanityVersion> GetKnownVanityVersions(NexusModsGameId nexusModsGameId);

    /// <summary>
    /// Gets all game files associated with the provided locator IDs.
    /// </summary>
    public IEnumerable<GameFileRecord> GetGameFiles(LocatorIdsWithGameStore locatorIdsWithGameStore);

    /// <summary>
    /// Tries to get a vanity version based on the locator IDs.
    /// </summary>
    public bool TryGetVanityVersion(LocatorIdsWithGameStore locatorIdsWithGameStore, out VanityVersion version);

    /// <summary>
    /// Tries to get all locator IDs for the given store and vanity version.
    /// </summary>
    public bool TryGetLocatorIdsForVanityVersion(GameStore gameStore, VanityVersion version, out LocatorId[] locatorIds);

    /// <summary>
    /// Gets all locator IDs for a given store and <see cref="VersionDefinition"/>.
    /// </summary>
    public LocatorId[] GetLocatorIdsForVersionDefinition(GameStore gameStore, VersionDefinition.ReadOnly versionDefinition);

    /// <summary>
    /// Suggest version data for a given game installation and files.
    /// </summary>
    public Optional<VersionData> SuggestVersionData(GameInstallation gameInstallation, IEnumerable<(GamePath Path, Hash Hash)> files);
}

/// <summary>
/// Tuple of many <see cref="LocatorId"/> and <see cref="Sdk.VanityVersion"/>.
/// </summary>
public record struct VersionData(LocatorId[] LocatorIds, VanityVersion VanityVersion);
