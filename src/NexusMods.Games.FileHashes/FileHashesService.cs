using System.Diagnostics;
using System.IO.Compression;
using System.IO.Hashing;
using System.Text.Json;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.EpicGameStore.Values;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.Abstractions.Games.FileHashes.Models;
using NexusMods.Abstractions.GOG.Values;
using NexusMods.Sdk.Settings;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Games.FileHashes.DTOs;
using NexusMods.Hashing.xxHash3;
using NexusMods.Hashing.xxHash3.Paths;
using NexusMods.Sdk.Hashes;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Storage;
using NexusMods.MnemonicDB.Storage.RocksDbBackend;
using NexusMods.Paths;
using NexusMods.Sdk;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.Jobs;
using NexusMods.Sdk.NexusModsApi;
using BuildId = NexusMods.Abstractions.GOG.Values.BuildId;
using Connection = NexusMods.MnemonicDB.Connection;

namespace NexusMods.Games.FileHashes;

internal sealed class FileHashesService : IFileHashesService, IDisposable, IHostedService
{
    private const string DefaultLanguage = "en-US";
    
    private readonly ScopedAsyncLock _lock = new();
    private readonly FileHashesServiceSettings _settings;
    private readonly IFileSystem _fileSystem;
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly Dictionary<AbsolutePath, ConnectedDb> _databases;
    private readonly IServiceProvider _provider;
    private readonly AbsolutePath _hashDatabaseLocation;

    /// <summary>
    /// The currently connected database (if any)
    /// </summary>
    private ConnectedDb? _currentDb;

    private readonly ILogger<FileHashesService> _logger;
    private readonly IQueryEngine _queryEngine;

    private record ConnectedDb(IDb Db, DatomStore Store, Backend Backend, DatabaseInfo DatabaseInfo);

    public FileHashesService(
        ILogger<FileHashesService> logger,
        ISettingsManager settingsManager,
        IFileSystem fileSystem,
        HttpClient httpClient,
        JsonSerializerOptions jsonSerializerOptions,
        IServiceProvider provider)
    {
        _logger = logger;
        _httpClient = httpClient;
        _jsonSerializerOptions = jsonSerializerOptions;
        _fileSystem = fileSystem;
        _settings = settingsManager.Get<FileHashesServiceSettings>();
        _databases = new Dictionary<AbsolutePath, ConnectedDb>();
        _provider = provider;
        _queryEngine = provider.GetRequiredService<IQueryEngine>();

        _hashDatabaseLocation = _settings.HashDatabaseLocation.ToPath(_fileSystem);
        _hashDatabaseLocation.CreateDirectory();
    }

    private ConnectedDb OpenDb(DatabaseInfo databaseInfo)
    {
        try
        {
            if (_databases.TryGetValue(databaseInfo.Path, out var existing))
                return existing;

            _logger.LogInformation("Opening hash database at {Path} for {Timestamp}", databaseInfo.Path, databaseInfo.CreationTime);
            var backend = new Backend(readOnly: true);
            var settings = new DatomStoreSettings
            {
                Path = databaseInfo.Path,
            };

            var store = new DatomStore(_provider.GetRequiredService<ILogger<DatomStore>>(), settings, backend);
            var connection = new Connection(_provider.GetRequiredService<ILogger<Connection>>(), store, _provider, [], readOnlyMode: true, prefix: "hashes", queryEngine: _queryEngine);
            var connectedDb = new ConnectedDb(connection.Db, store, backend, databaseInfo);

            _databases[databaseInfo.Path] = connectedDb;
            return connectedDb;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening database at {Path}", databaseInfo.Path);
            throw;
        }
    }

    private record struct DatabaseInfo(AbsolutePath Path, DateTimeOffset CreationTime);

    private IEnumerable<DatabaseInfo> ExistingDBs()
    {
        return _hashDatabaseLocation
            .EnumerateDirectories(recursive: false)
            .Where(d => !d.FileName.EndsWith("_tmp"))
            .Select(path =>
            {
                // Format is "{guid}_{timestamp}"
                var parts = path.FileName.Split('_');
                if (parts.Length != 2 || !ulong.TryParse(parts[1], out var timestamp)) return Optional<DatabaseInfo>.None;
                return new DatabaseInfo(Path: path, CreationTime: DateTimeOffset.FromUnixTimeSeconds((long)timestamp));
            })
            .Where(static optional => optional.HasValue)
            .Select(static optional => optional.Value)
            .OrderByDescending(static databaseInfo => databaseInfo.CreationTime);
    }

    /// <inheritdoc />
    public async Task CheckForUpdate(bool forceUpdate = false)
    {
        await CheckForUpdateCore(forceUpdate);
    }

    /// <inheritdoc />
    public IEnumerable<VanityVersion> GetKnownVanityVersions(NexusModsGameId nexusModsGameId)
    {
        return GetVersionDefinitions(nexusModsGameId)
            .Select(v => VanityVersion.From(v.Name))
            .ToList();
    }

    private List<VersionDefinition.ReadOnly> GetVersionDefinitions(NexusModsGameId nexusModsGameId)
    {
        // MIST-006-04D.2: Historical versions remain available across
        // retained read-only game-hash snapshots.
        return HistoricalDatabases()
            .SelectMany(database => VersionDefinition.All(database.Db))
            .Where(v => v.GameId == nexusModsGameId)
            .DistinctBy(v => (v.Name, string.Join(",", v.SteamManifests
                .Select(manifest => manifest.ManifestId.Value).OrderBy(id => id))))
            .ToList();
    }

    /// <summary>
    /// Yield the active database followed by retained historical snapshots.
    /// These databases are read-only. One broken historical snapshot does not
    /// prevent M.I.S.T. from recognizing games using the remaining snapshots.
    /// </summary>
    private IEnumerable<ConnectedDb> HistoricalDatabases()
    {
        HashSet<AbsolutePath> visited = [];
        if (_currentDb is not null && visited.Add(_currentDb.DatabaseInfo.Path))
            yield return _currentDb;

        foreach (var info in ExistingDBs())
        {
            if (!visited.Add(info.Path)) continue;

            ConnectedDb database;
            try
            {
                database = OpenDb(info);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception,
                    "Skipping unreadable historical hash database at {Path}", info.Path);
                continue;
            }

            yield return database;
        }
    }

    private sealed record SteamSnapshotMatch(
        ConnectedDb Database,
        string Fingerprint,
        bool HasVersion,
        VersionDefinition.ReadOnly Definition)
    {
        public string? VersionName => HasVersion ? Definition.Name : null;
    }

    /// <summary>
    /// Select one consistent original-file baseline and, when available,
    /// a unique public version definition. Do not mix file records across
    /// different snapshots or trust a matching depot in isolation.
    /// </summary>
    private bool TryResolveSteamSnapshot(
        IEnumerable<LocatorId> locatorIds, out SteamSnapshotMatch? selected)
    {
        selected = null;
        var installed = new HashSet<ulong>();
        foreach (var locatorId in locatorIds)
        {
            if (!ulong.TryParse(locatorId.Value, out var id) || id == 0)
                return false;
            installed.Add(id);
        }

        if (installed.Count == 0) return false;

        var candidates = new List<SteamSnapshotMatch>();
        foreach (var database in HistoricalDatabases())
        {
            var manifests = new List<SteamManifest.ReadOnly>();
            foreach (var id in installed)
            {
                var records = SteamManifest.FindByManifestId(
                    database.Db, ManifestId.From(id)).Take(2).ToArray();
                if (records.Length != 1 || !records[0].Files.Any())
                {
                    manifests.Clear();
                    break;
                }
                manifests.Add(records[0]);
            }

            if (manifests.Count != installed.Count ||
                manifests.Select(m => m.AppId.Value).Distinct().Skip(1).Any())
                continue;

            // Include every original file's identity in the fingerprint.
            // Equal manifest IDs with contradictory file records are NOT
            // interchangeable baselines.
            var rows = manifests.SelectMany(m => m.Files.Select(f =>
                    $"{m.AppId.Value}|{m.ManifestId.Value}|{f.Path}|{f.Hash.Size}|{f.Hash.XxHash3}"))
                .OrderBy(line => line, StringComparer.Ordinal);
            var fingerprint = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(string.Join("\n", rows))));

            var matchingVersions = VersionDefinition.All(database.Db)
                .Where(definition => installed.SetEquals(
                    definition.SteamManifests.Select(m => m.ManifestId.Value)))
                .Take(2)
                .ToArray();
            if (matchingVersions.Length > 1)
            {
                _logger.LogWarning(
                    "Ambiguous historical version definitions for Steam manifests {Manifests}",
                    string.Join(",", installed.OrderBy(x => x)));
                return false;
            }

            candidates.Add(new SteamSnapshotMatch(
                database, fingerprint, matchingVersions.Length == 1,
                matchingVersions.Length == 1
                    ? matchingVersions[0] : default(VersionDefinition.ReadOnly)));
        }

        if (!MISTSteamHistoricalSnapshotSelector.TrySelect(
                candidates.Select(c => (c.Fingerprint, c.VersionName)).ToArray(),
                out var selectedIndex, out _))
        {
            return false;
        }

        selected = candidates[selectedIndex];
        return true;
    }

    /// <inheritdoc />
    public Task MISTCaptureInstalledSteamManifestsAsync(
        string gameFolder, CancellationToken cancellationToken = default)
    {
        // Steam depot metadata may be large; never block the UI thread or
        // touch any deployed game file. Only the separate app-side catalog
        // is written. Automatic capture is not automatic compatibility.
        return Task.Run(() =>
        {
            try
            {
                var appData = Path.GetDirectoryName(_hashDatabaseLocation.ToString());
                if (string.IsNullOrWhiteSpace(appData)) return;
                var catalogRoot = Path.Combine(appData, "MISTSteamManifestCatalog");
                var capture = MISTSteamDepotManifestImporter.Capture(
                    gameFolder, catalogRoot, cancellationToken);
                if (capture is null) return;
                _logger.LogInformation(
                    "MIST-006-04D3: App={AppId} Build={BuildId} Depots={Depots} Missing={Missing} Complete={Complete}",
                    capture.AppId, capture.BuildId, capture.Depots.Count,
                    capture.MissingDepots.Count, capture.Complete);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                _logger.LogWarning(exception,
                    "MIST-006-04D3: Could not capture local Steam depot metadata for {GamePath}", gameFolder);
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<Optional<GameFileRecord>> MISTFindVerifiedOriginalSteamFileAsync(
        GameInstallation installation, GamePath path,
        CancellationToken cancellationToken = default)
    {
        // This is an explicit, read-only per-file query, not a startup scan,
        // loadout reset baseline or filesystem mutation permission.
        if (installation.LocatorResult.Store != GameStore.Steam ||
            path.LocationId != LocationId.Game)
            return Optional<GameFileRecord>.None;

        var gameFolder = installation.Locations[LocationId.Game].Path.ToString();
        var appData = Path.GetDirectoryName(_hashDatabaseLocation.ToString());
        if (string.IsNullOrEmpty(appData))
            return Optional<GameFileRecord>.None;

        var catalogRoot = Path.Combine(appData, "MISTSteamManifestCatalog");
        if (!MISTSteamVerifiedOriginalCatalog.TryLoadComplete(
                gameFolder, catalogRoot, out var identity, out var catalog))
            return Optional<GameFileRecord>.None;

        var expected = await MISTSteamVerifiedOriginalCatalog.VerifyInstalledFileAsync(
            gameFolder, catalog!, identity!, path.Path.ToString(), cancellationToken);
        if (expected is null) return Optional<GameFileRecord>.None;

        // Steam provides SHA-1, whereas existing Nexus consumers expect
        // xxHash3 (both full and minimal). Calculate those only AFTER SHA-1
        // confirms installed bytes match the Steam manifest entry.
        // This intentionally does not install these records as a global
        // original-file baseline: the global API lacks installation identity.
        var absolute = installation.Locations.ToAbsolutePath(path);
        try
        {
            var beforeSize = new FileInfo(absolute.ToString()).Length;
            var beforeWrite = absolute.FileInfo.LastWriteTimeUtc;
            if (beforeSize != expected.Size)
                return Optional<GameFileRecord>.None;

            var fullHash = await absolute.XxHash3Async(token: cancellationToken);
            await using var stream = absolute.Read();
            var minimalHash = await MinimalHash.HashAsync<Hash, XxHash3, Xx3Hasher>(
                stream, cancellationToken: cancellationToken);

            // If another process modified the file during the second/third
            // hash pass, reject the evidence rather than caching stale data.
            if (new FileInfo(absolute.ToString()).Length != beforeSize ||
                absolute.FileInfo.LastWriteTimeUtc != beforeWrite)
                return Optional<GameFileRecord>.None;

            return new GameFileRecord
            {
                Path = path,
                Size = absolute.FileInfo.Size,
                MinimalHash = minimalHash,
                Hash = fullHash,
            };
        }
        catch (IOException) { return Optional<GameFileRecord>.None; }
        catch (UnauthorizedAccessException) { return Optional<GameFileRecord>.None; }
    }

    private bool ShouldCheckForUpdate()
    {
        if (!GameHashesReleaseFileName.FileExists) return true;
        var lastUpdated = GameHashesReleaseFileName.FileInfo.LastWriteTimeUtc;
        var diff = DateTime.UtcNow - lastUpdated;
        return diff >= _settings.HashDatabaseUpdateInterval;
    }


    private async Task CheckForUpdateCore(bool forceUpdate, CancellationToken cancellationToken = default)
    {
        using var _ = await _lock.LockAsync();

        var existingDatabases = ExistingDBs().ToArray();
        var shouldCheckForUpdate = forceUpdate || existingDatabases.Length == 0 || ShouldCheckForUpdate();

        if (!shouldCheckForUpdate && existingDatabases.TryGetFirst(out var latestDatabase))
        {
            _currentDb = OpenDb(latestDatabase);
            return;
        }

        Manifest? latestReleaseManifest = null;
        if (shouldCheckForUpdate)
        {
            latestReleaseManifest = await FetchLatestReleaseManifest(GameHashesReleaseFileName, cancellationToken: cancellationToken);
        }

        if (existingDatabases.Length == 0)
        {
            var embeddedDatabaseInfo = await AddEmbeddedDatabase(cancellationToken: cancellationToken);
            if (latestReleaseManifest is null)
            {
                if (!embeddedDatabaseInfo.HasValue)
                {
                    _logger.LogError("Failed to add embedded game hashes database and failed to fetch latest release manifest, game hashes functionality will be unavailable");
                    return;
                }

                _logger.LogWarning("Failed to fetch latest release manifest, defaulting to embedded game hashes database which may be out-of-date");
                _currentDb = OpenDb(embeddedDatabaseInfo.Value);
                return;
            }

            Debug.Assert(latestReleaseManifest is not null, "should've returned if we didn't have a manifest");
            if (embeddedDatabaseInfo.HasValue)
            {
                existingDatabases = ExistingDBs().ToArray();
                Debug.Assert(existingDatabases.Length >= 1, $"should have at least one database but found {existingDatabases.Length}");
            }
        }

        if (latestReleaseManifest is null && existingDatabases.Length == 0)
        {
            _logger.LogError("Failed to fetch the latest release manifest and failed to use the embedded database, game hashes functionality will be unavailable");
            return;
        }

        if (latestReleaseManifest is null || existingDatabases[0].CreationTime.ToUnixTimeSeconds() >= latestReleaseManifest.CreatedAt.ToUnixTimeSeconds())
        {
            _currentDb = OpenDb(existingDatabases[0]);
            return;
        }

        _logger.LogInformation("Fetching latest games hashes database");
        var releaseDatabaseInfo = await AddReleaseDatabase(latestReleaseManifest, cancellationToken);
        if (!releaseDatabaseInfo.HasValue) return;

        _currentDb = OpenDb(releaseDatabaseInfo.Value);
    }

    private async ValueTask<Manifest?> FetchLatestReleaseManifest(AbsolutePath storagePath, CancellationToken cancellationToken)
    {
        const int defaultTimeout = 15;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(delay: TimeSpan.FromSeconds(defaultTimeout));

        try
        {
            await using var fileStream = storagePath.Create();
            await using (var httpStream = await _httpClient.GetStreamAsync(_settings.GithubManifestUrl, cancellationToken: cts.Token))
            {
                await httpStream.CopyToAsync(fileStream, cancellationToken: cts.Token);
            }

            fileStream.Position = 0;
            return await JsonSerializer.DeserializeAsync<Manifest>(fileStream, _jsonSerializerOptions, cancellationToken: cts.Token);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to fetch latest release manifest from `{Url}`", _settings.GithubManifestUrl);
            return null;
        }
    }

    private async ValueTask<Optional<DatabaseInfo>> AddEmbeddedDatabase(CancellationToken cancellationToken)
    {
        try
        {
            var streamFactory = new EmbeddedResourceStreamFactory<FileHashesService>(resourceName: "games_hashes_db.zip");
            await using var archiveStream = await streamFactory.GetStreamAsync();
            var creationTime = ApplicationConstants.IsDebug ? DateTimeOffset.UnixEpoch : ApplicationConstants.BuildDate;

            var path = await AddDatabase(archiveStream, creationTime, cancellationToken);
            return new DatabaseInfo(path, creationTime);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to add embedded database");
            return Optional<DatabaseInfo>.None;
        }
    }

    private async ValueTask<Optional<DatabaseInfo>> AddReleaseDatabase(Manifest releaseManifest, CancellationToken cancellationToken)
    {
        try
        {
            await using var httpStream = await _httpClient.GetStreamAsync(_settings.GameHashesDbUrl, cancellationToken: cancellationToken);
            var path = await AddDatabase(httpStream, releaseManifest.CreatedAt, cancellationToken: cancellationToken);
            return new DatabaseInfo(path, releaseManifest.CreatedAt);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to add release database from {Url}", _settings.GameHashesDbUrl);
            return Optional<DatabaseInfo>.None;
        }
    }

    private async ValueTask<AbsolutePath> AddDatabase(
        Stream archiveStream,
        DateTimeOffset databaseCreationTime,
        CancellationToken cancellationToken)
    {
        var name = $"{Guid.NewGuid()}_{databaseCreationTime.ToUnixTimeSeconds()}";

        await using var archivePath = new TemporaryPath(_fileSystem, _hashDatabaseLocation / $"{name}.zip");
        await using (var fileStream = archivePath.Path.Create())
        {
            await archiveStream.CopyToAsync(fileStream, cancellationToken: cancellationToken);
        }

        await using var extractionDirectory = new TemporaryPath(_fileSystem, _hashDatabaseLocation / $"{name}_tmp");
        await using (var fileStream = archivePath.Path.Read())
        using (var zipArchive = new ZipArchive(fileStream, ZipArchiveMode.Read))
        {
            foreach (var fileEntry in zipArchive.Entries)
            {
                var destinationPath = extractionDirectory.Path.Combine(fileEntry.FullName);
                destinationPath.Parent.CreateDirectory();

                await using var entryStream = fileEntry.Open();
                await using var outputStream = destinationPath.Create();
                await entryStream.CopyToAsync(outputStream, cancellationToken: cancellationToken);
            }
        }

        var finalDirectory = _hashDatabaseLocation / name;
        Directory.Move(
            sourceDirName: extractionDirectory.Path.ToNativeSeparators(OSInformation.Shared),
            destDirName: finalDirectory.ToNativeSeparators(OSInformation.Shared)
        );

        return finalDirectory;
    }

    private AbsolutePath GameHashesReleaseFileName => _hashDatabaseLocation / _settings.ReleaseFilePath;

    /// <inheritdoc />
    public async ValueTask<IDb> GetFileHashesDb()
    {
        if (_currentDb is not null)
            return _currentDb.Db;

        // Call core since we're already inside a lock
        await CheckForUpdateCore(false);

        return Current;
    }

    /// <inheritdoc/>
    public IEnumerable<GameFileRecord> GetGameFiles(LocatorIdsWithGameStore locatorIdsWithGameStore)
    {
        var (gameStore, locatorIds) = locatorIdsWithGameStore;

        if (gameStore == GameStore.GOG)
        {
            HashSet<GogBuild.ReadOnly> gogBuilds = [];
            HashSet<ProductId> gogProducts = [];
            Dictionary<EntityId, GogManifest.ReadOnly> gogManifests = [];
            
            // So first we find all the valid build Ids, and then assume that everything else is a product Id
            foreach (var id in locatorIds)
            {
                if (!ulong.TryParse(id.Value, out var parsedId))
                    continue;

                var gogId = BuildId.From(parsedId);

                if (GogBuild.FindByBuildId(Current, gogId).TryGetFirst(out var firstBuild))
                {
                    gogBuilds.Add(firstBuild);
                    continue;
                }
                
                var productId = ProductId.From(parsedId);
                gogProducts.Add(productId);
            }
            
            // Now we emit all the files from the build products, and then also from any secondary products
            foreach (var build in gogBuilds)
            {
                foreach (var depot in build.Depots)
                {
                    // We only care about the productId of the build, and the productIds of the secondary products
                    if (!(depot.ProductId == build.ProductId || gogProducts.Contains(depot.ProductId)))
                        continue;
                    
                    // If there is a language setting for the files, they have to be the same as the default language
                    if (!(depot.Languages.Count == 0 || depot.Languages.Contains(DefaultLanguage)))
                        continue;

                    gogManifests[depot.Manifest.Id] = depot.Manifest;
                }
            }

            foreach (var (_ , manifest) in gogManifests)
            {
                foreach (var file in manifest.Files)
                {
                    yield return new GameFileRecord
                    {
                        Path = (LocationId.Game, file.Path),
                        Size = file.Hash.Size,
                        MinimalHash = file.Hash.MinimalHash,
                        Hash = file.Hash.XxHash3,
                    };
                }
            }
            
        }
        else if (gameStore == GameStore.Steam)
        {
            // All original-file records must come from the SAME consistent
            // snapshot selected for this exact installed depot manifest set.
            if (!TryResolveSteamSnapshot(locatorIds, out var snapshot))
                yield break;

            foreach (var id in locatorIds.Distinct())
            {
                if (!ulong.TryParse(id.Value, out var parsedId)) continue;
                var records = SteamManifest.FindByManifestId(
                    snapshot!.Database.Db, ManifestId.From(parsedId)).Take(2).ToArray();
                if (records.Length != 1) continue;

                foreach (var file in records[0].Files)
                {
                    yield return new GameFileRecord
                    {
                        Path = (LocationId.Game, file.Path),
                        Size = file.Hash.Size,
                        MinimalHash = file.Hash.MinimalHash,
                        Hash = file.Hash.XxHash3,
                    };
                }
            }
        }
        else if (gameStore == GameStore.EGS)
        {
            foreach (var manifestId in locatorIds)
            {
                var egManifestId = ManifestHash.FromUnsanitized(manifestId.Value);
                
                if (!EpicGameStoreBuild.FindByManifestHash(Current, egManifestId).TryGetFirst(out var firstManifest))
                {
                    _logger.LogWarning("No EGS manifest found for {ManifestId}", egManifestId.Value);
                    continue;
                }
                
                foreach (var file in firstManifest.Files)
                {
                    yield return new GameFileRecord
                    {
                        Path = (LocationId.Game, file.Path),
                        Size = file.Hash.Size,
                        MinimalHash = file.Hash.MinimalHash,
                        Hash = file.Hash.XxHash3,
                    };
                }
            }
        }
        else
        {
            throw new NotSupportedException("No way to get game files for: " + gameStore);
        }
    }

    /// <inheritdoc />
    public IDb Current => _currentDb?.Db ?? throw new InvalidOperationException("No database connected");

    /// <inheritdoc />
    public bool TryGetVanityVersion(LocatorIdsWithGameStore locatorIdsWithGameStore, out VanityVersion version)
    {
        var (gameStore, locatorIds) = locatorIdsWithGameStore;

        // The immutable M.I.S.T. aliases are curated evidence, not a guess
        // based on a Steam build ID. Exact depot-manifest equality is required.
        // Both UI version labels and loadout creation use this service.
        string? curatedVersion = null;
        var hasCuratedVersion = gameStore == GameStore.Steam &&
            MISTSteamPublicVersionCatalog.TryResolve(
                locatorIds.Select(x => x.Value), out curatedVersion);

        if (TryGetGameVersionDefinition(locatorIdsWithGameStore, out var versionDefinition))
        {
            // A conflicting alias is an error in our data; don't silently
            // tell compatibility checks that one of the labels is correct.
            if (hasCuratedVersion && !string.Equals(
                    curatedVersion, versionDefinition.Name, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Conflicting public versions for Steam manifests: historical={Historical}, curated={Curated}",
                    versionDefinition.Name, curatedVersion);
                version = VanityVersion.DefaultValue;
                return false;
            }

            version = VanityVersion.From(versionDefinition.Name);
            return true;
        }

        if (hasCuratedVersion)
        {
            version = VanityVersion.From(curatedVersion!);
            return true;
        }

        version = VanityVersion.DefaultValue;
        return false;
    }

    private bool TryGetGameVersionDefinition(
        LocatorIdsWithGameStore locatorIdsWithGameStore,
        out VersionDefinition.ReadOnly versionDefinition)
    {
        var (gameStore, locatorIds) = locatorIdsWithGameStore;

        versionDefinition = default(VersionDefinition.ReadOnly);
        if (gameStore == GameStore.GOG)
        {
            List<GogBuild.ReadOnly> gogBuilds = [];

            foreach (var gogId in locatorIds)
            {
                if (!ulong.TryParse(gogId.Value, out var parsedId))
                {
                    _logger.LogWarning("Unable to parse `{Raw}` as ulong", gogId);
                    return false;
                }

                var hasBuild = GogBuild.FindByBuildId(Current, BuildId.From(parsedId)).TryGetFirst(out var gogBuild);
                if (hasBuild) gogBuilds.Add(gogBuild);
            }

            if (gogBuilds.Count == 0)
            {
                _logger.LogDebug("No GOG builds found");
                return false;
            }

            var hasVersionDefinition = VersionDefinition.All(_currentDb!.Db)
                .Select(version =>
                {
                    var matchingIdCount = gogBuilds.Count(g => version.GogBuildsIds.Contains(g));
                    return (matchingIdCount, version);
                })
                .Where(row => row.matchingIdCount > 0)
                .OrderByDescending(row => row.matchingIdCount)
                .Select(t => t.version)
                .TryGetFirst(out versionDefinition);

            if (!hasVersionDefinition)
            {
                _logger.LogDebug("No matching version definition found");
                return false;
            }
        }
        else if (gameStore == GameStore.Steam)
        {
            if (!TryResolveSteamSnapshot(locatorIds, out var snapshot) ||
                !snapshot!.HasVersion)
            {
                _logger.LogDebug("No unique complete Steam version in retained hash databases");
                return false;
            }

            versionDefinition = snapshot.Definition;
        }
        else if (gameStore == GameStore.EGS)
        {
            var versionsByManifestHash = VersionDefinition.All(_currentDb!.Db)
                .SelectMany(version =>
                    {
                        if (VersionDefinition.EpicGameStoreBuildsIds.IsIn(version)) 
                            return version.EpicGameStoreBuilds.Select(build => (Version: version, Build: build));
                        return [];
                    }
                )
                .ToLookup(row => row.Build.ManifestHash);

            var builds = locatorIds
                .Select(locatorString => ManifestHash.FromUnsanitized(locatorString.Value))
                .SelectMany(manifestHash => versionsByManifestHash[manifestHash].Select(row => (row.Version, manifestHash)));

            if (builds.TryGetFirst(out var build))
            {
                versionDefinition = build.Version;
                return true;
            }

            return false;
        }
        else
        {
            _logger.LogDebug("No way to get game version for: {Store}", gameStore);
            return false;
        }

        return true;
    }

    /// <inheritdoc />
    public bool TryGetLocatorIdsForVanityVersion(GameStore gameStore, VanityVersion version, out LocatorId[] commonIds)
    {
        // A public version may live in a retained historical snapshot.
        // Reject incompatible identically named definitions instead of
        // selecting one arbitrarily from a different game.
        LocatorId[]? matched = null;
        foreach (var database in HistoricalDatabases())
        {
            foreach (var definition in VersionDefinition.FindByName(database.Db, version.Value))
            {
                var ids = GetLocatorIdsForVersionDefinition(gameStore, definition);
                if (ids.Length == 0) continue;
                if (matched is not null &&
                    !matched.Select(x => x.Value).OrderBy(x => x)
                        .SequenceEqual(ids.Select(x => x.Value).OrderBy(x => x)))
                {
                    commonIds = [];
                    return false;
                }
                matched = ids;
            }
        }

        commonIds = matched ?? [];
        return matched is not null;
    }

    public LocatorId[] GetLocatorIdsForVersionDefinition(GameStore gameStore, VersionDefinition.ReadOnly versionDefinition)
    {
        if (gameStore == GameStore.GOG)
        {
            return versionDefinition.GogBuilds.Select(build => LocatorId.From(build.BuildId!.Value.ToString())).ToArray();
        }

        if (gameStore == GameStore.Steam)
        {
            return versionDefinition.SteamManifests.Select(manifest => LocatorId.From(manifest.ManifestId.ToString())).ToArray();
        }
        
        if (gameStore == GameStore.EGS)
        {
            return versionDefinition.EpicGameStoreBuilds.Select(build => LocatorId.From(build.ManifestHash.Value)).ToArray();
        }

        throw new NotSupportedException("No way to get common IDs for: " + gameStore);
    }

    /// <inheritdoc />
    public Optional<VersionData> SuggestVersionData(GameInstallation gameInstallation, IEnumerable<(GamePath Path, Hash Hash)> files)
    {
        var filesSet = files.ToHashSet();

        List<(VersionData VersionData, int Matches)> versionMatches = [];
        foreach (var versionDefinition in GetVersionDefinitions(gameInstallation.Game.NexusModsGameId.Value))
        {
            var locatorIds = GetLocatorIdsForVersionDefinition(gameInstallation.LocatorResult.Store, versionDefinition);

            var matchingCount = GetGameFiles((gameInstallation.LocatorResult.Store, locatorIds))
                .Count(file => filesSet.Contains((file.Path, file.Hash)));

            versionMatches.Add((new VersionData(locatorIds, VanityVersion.From(versionDefinition.Name)), matchingCount));
        }

        return versionMatches
            .OrderByDescending(t => t.Matches)
            .Select(t => t.VersionData)
            .FirstOrOptional(_ => true);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var connection in _databases.Values)
        {
            connection.Backend.Dispose();
            connection.Store.Dispose();
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var existingDatabases = ExistingDBs().ToArray();

        // MIST-006-04D.2: Retain historical hash snapshots. Users can
        // maintain older game builds and their associated loadouts.
        // Never remove previous snapshots automatically at startup.

        var forceUpdate = false;
        try
        {
            if (existingDatabases.TryGetFirst(out var latestDatabase))
            {
                _currentDb = OpenDb(latestDatabase);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to open latest database, forcing update");
            forceUpdate = true;
        }

        await CheckForUpdateCore(forceUpdate: forceUpdate, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
