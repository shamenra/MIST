using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using GameFinder.StoreHandlers.Steam;
using GameFinder.StoreHandlers.Steam.Services;
using GameFinder.StoreHandlers.Steam.Models;
using Microsoft.Extensions.Logging;
using NexusMods.Paths;
using NexusMods.Sdk;
using NexusMods.Sdk.Games;

namespace NexusMods.Backend.Games.Locators;

internal class SteamLocator : IGameLocator
{
    private readonly ILogger _logger;
    private readonly SteamHandler _handler;
    private readonly IFileSystem _fileSystem;
    private readonly FrozenDictionary<uint, IGameData> _registeredGames;

    private static readonly GameStore Store = GameStore.Steam;

    public SteamLocator(IEnumerable<IGameData> games, ILoggerFactory loggerFactory, IFileSystem fileSystem, GameFinder.RegistryUtils.IRegistry? registry)
    {
        _logger = loggerFactory.CreateLogger<SteamLocator>();
        _fileSystem = fileSystem;

        _handler = new SteamHandler(
            fileSystem: fileSystem,
            registry: registry,
            logger: loggerFactory.CreateLogger<SteamHandler>()
        );

        _registeredGames = games
            .SelectMany(game => game.StoreIdentifiers.SteamAppIds, (game, storeIdentifier) => new KeyValuePair<uint, IGameData>(storeIdentifier, game))
            .ToFrozenDictionary();
    }

    public IEnumerable<GameLocatorResult> Locate()
    {
        // MIST-006-02A: Opt-in discovery diagnostics.
        // Does not modify Steam configuration or game files.
        var mistTrace = Environment.GetEnvironmentVariable(
            "MIST_TRACE_STEAM") == "1";

        var mistDiscovered = 0;
        var mistMatched = 0;
        var mistErrors = 0;

        // MIST-006-02B:
        // Discover and remember Steam libraries separately from
        // GameFinder's installation detection.
        //
        // This first integration is catalog-only. It does not
        // authorize deployments or replace GameFinder results.
        // MIST-006-02B.2: Retain catalog results so
        // additional libraries can supply Steam manifests.
        MISTSteamLibraryCatalog? mistCatalog = null;
        IReadOnlyList<MISTSteamLibraryStatus> mistLibraries = [];

        try
        {
            var libraryCatalog =
                MISTSteamLibraryCatalog.CreateForCurrentUser();

            mistCatalog = libraryCatalog;

            // Temporary developer entry point until the
            // Add Library UI is implemented.
            var manuallyAddedPath =
                Environment.GetEnvironmentVariable(
                    "MIST_STEAM_LIBRARY_ADD");

            if (!string.IsNullOrWhiteSpace(manuallyAddedPath))
                libraryCatalog.AddManual(manuallyAddedPath);

            var libraries = libraryCatalog.Refresh();
            mistLibraries = libraries;

            if (mistTrace)
            {
                foreach (var library in libraries)
                {
                    _logger.LogInformation(
                        "MIST-006-02: Library Path={Path}, " +
                        "Online={Online}, UserAdded={UserAdded}, " +
                        "Manifests={Manifests}",
                        library.Path,
                        library.IsOnline,
                        library.UserAdded,
                        library.ManifestCount);
                }

                _logger.LogInformation(
                    "MIST-006-02: Library catalog complete. " +
                    "Libraries={Count}, Online={Online}",
                    libraries.Count,
                    libraries.Count(x => x.IsOnline));
            }
        }
        catch (Exception error) when (
            error is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            JsonException)
        {
            // Failure to read the optional M.I.S.T. catalog
            // must not break the inherited Steam locator.
            _logger.LogWarning(
                error,
                "MIST-006-02: Steam library catalog unavailable.");
        }


        if (mistTrace)
        {
            _logger.LogInformation(
                "MIST-006-02: Steam discovery starting. " +
                "HOME={Home}, XDG_DATA_HOME={Xdg}, " +
                "RegisteredSteamGames={Registered}",
                Environment.GetEnvironmentVariable("HOME"),
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                _registeredGames.Count);
        }

        var mistSeen = new HashSet<string>(
            StringComparer.Ordinal);

        foreach (var result in _handler.FindAllGames())
        {
            if (result.TryPickT1(out var errorMessage, out var gameFinderGame))
            {
                mistErrors++;
                _logger.LogWarning("Error locating games: {ErrorMessage}", errorMessage.Message);
                continue;
            }

            var storeIdentifier = gameFinderGame.AppId.Value;
            _logger.LogDebug("Found game '{GameName}' with store identifier '{StoreIdentifier}'", gameFinderGame.Name, storeIdentifier);

            mistDiscovered++;

            if (mistTrace && mistDiscovered <= 12)
            {
                _logger.LogInformation(
                    "MIST-006-02: GameFinder found " +
                    "AppId={AppId}, Name={Name}, Path={Path}",
                    storeIdentifier,
                    gameFinderGame.Name,
                    gameFinderGame.Path);
            }

            if (!_registeredGames.TryGetValue(
                    storeIdentifier, out var game))
                continue;

            mistMatched++;

            if (mistTrace)
            {
                _logger.LogInformation(
                    "MIST-006-02: SUPPORTED MATCH " +
                    "AppId={AppId}, Name={Name}",
                    storeIdentifier,
                    gameFinderGame.Name);
            }

            if (!mistSeen.Add(
                    MISTGameKey(gameFinderGame)))
                continue;

            yield return MISTCreateLocatorResult(
                gameFinderGame,
                game,
                storeIdentifier);
        }

        // MIST-006-02B.2:
        // GameFinder's default handler found the native root.
        // Scan additional registered libraries using its
        // public manifest parser and existing SteamGame model.
        var mistCatalogMatches = 0;

        if (mistCatalog is not null)
        {
            foreach (var library in mistLibraries)
            {
                if (!library.IsOnline)
                    continue;

                var steamRoot =
                    mistCatalog.FindSteamRootForLibrary(
                        library.Path);

                if (steamRoot is null)
                {
                    // User-added libraries may not be listed
                    // in any current Steam installation.
                    // Their Proton association is unverified.
                    steamRoot = library.Path;

                    if (mistTrace)
                    {
                        _logger.LogWarning(
                            "MIST-006-02: No associated " +
                            "Steam root for library {Library}",
                            library.Path);
                    }
                }

                foreach (var (appId, game) in
                         _registeredGames)
                {
                    var steamGame = MISTReadCatalogGame(
                        library.Path,
                        steamRoot,
                        appId);

                    if (steamGame is null)
                        continue;

                    if (!mistSeen.Add(
                            MISTGameKey(steamGame)))
                        continue;

                    mistCatalogMatches++;

                    if (mistTrace)
                    {
                        _logger.LogInformation(
                            "MIST-006-02: CATALOG MATCH " +
                            "AppId={AppId}, Name={Name}, " +
                            "Path={Path}",
                            appId,
                            steamGame.Name,
                            steamGame.Path);
                    }

                    yield return MISTCreateLocatorResult(
                        steamGame,
                        game,
                        appId);
                }
            }
        }

        if (mistTrace)
        {
            _logger.LogInformation(
                "MIST-006-02: Steam discovery complete. " +
                "NativeDiscovered={NativeDiscovered}, " +
                "NativeSupported={NativeSupported}, " +
                "CatalogSupported={CatalogSupported}, " +
                "TotalSupported={TotalSupported}, " +
                "Errors={Errors}",
                mistDiscovered,
                mistMatched,
                mistCatalogMatches,
                mistSeen.Count,
                mistErrors);
        }
    }

    private static string MISTGameKey(SteamGame steamGame)
    {
        return steamGame.AppId.Value.ToString() + "|" +
               System.IO.Path.GetFullPath(
                   steamGame.Path.ToString());
    }

    /// <summary>
    /// Reads one supported game manifest from an additional
    /// Steam library using GameFinder's own parser.
    ///
    /// Never writes to Steam or the game installation.
    /// </summary>
    private SteamGame? MISTReadCatalogGame(
        string libraryPath,
        string steamRoot,
        uint expectedAppId)
    {
        var manifestFile = System.IO.Path.Combine(
            libraryPath,
            "steamapps",
            $"appmanifest_{expectedAppId}.acf");

        if (!System.IO.File.Exists(manifestFile))
            return null;

        try
        {
            var result = AppManifestParser.ParseManifestFile(
                _fileSystem.FromUnsanitizedFullPath(
                    manifestFile));

            if (result.IsFailed)
            {
                _logger.LogWarning(
                    "MIST-006-02: Could not parse catalog " +
                    "manifest {Manifest}: {Error}",
                    manifestFile,
                    result.ToString());

                return null;
            }

            var manifest = result.Value;

            if (manifest.AppId.Value != expectedAppId)
            {
                _logger.LogWarning(
                    "MIST-006-02: Manifest AppId mismatch " +
                    "at {Manifest}",
                    manifestFile);

                return null;
            }

            var steamGame = new SteamGame
            {
                AppManifest = manifest,
                LibraryFolder = new LibraryFolder
                {
                    Path = _fileSystem.FromUnsanitizedFullPath(
                        libraryPath)
                },
                SteamPath = _fileSystem.FromUnsanitizedFullPath(
                    steamRoot)
            };

            // Do not register manifest-supplied paths outside
            // the expected Steam common directory.
            var common = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(
                    libraryPath,
                    "steamapps",
                    "common"));

            var gamePath = System.IO.Path.GetFullPath(
                steamGame.Path.ToString());

            var expectedPrefix =
                common.TrimEnd(
                    System.IO.Path.DirectorySeparatorChar)
                + System.IO.Path.DirectorySeparatorChar;

            if (!gamePath.StartsWith(
                    expectedPrefix,
                    StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "MIST-006-02: Rejected installation " +
                    "path outside Steam library: {Path}",
                    gamePath);

                return null;
            }

            if (!System.IO.Directory.Exists(gamePath))
                return null;

            return steamGame;
        }
        catch (Exception error) when (
            error is System.IO.IOException or
            UnauthorizedAccessException or
            ArgumentException or
            InvalidOperationException)
        {
            _logger.LogWarning(
                error,
                "MIST-006-02: Could not inspect catalog " +
                "manifest {Manifest}",
                manifestFile);

            return null;
        }
    }

    /// <summary>
    /// Converts a parsed SteamGame using the same metadata
    /// rules for native and additional libraries.
    /// </summary>
    private GameLocatorResult MISTCreateLocatorResult(
        SteamGame gameFinderGame,
        IGameData game,
        uint storeIdentifier)
    {
        var path = gameFinderGame.Path;

        // MIST-004A: Preserve installed depot/manifest associations.
        var installedSteamDepots = gameFinderGame.AppManifest.InstalledDepots
            .Select(x => new SteamInstalledDepot(
                x.Key.Value,
                x.Value.ManifestId.Value))
            .ToImmutableArray();

        // Keep existing manifest-only locator IDs for compatibility.
        var locatorIds = installedSteamDepots
            .Select(x => LocatorId.From(x.ManifestId.ToString()))
            .ToImmutableArray();

        var winePrefixDirectoryPath = gameFinderGame.GetProtonPrefix()?.ProtonDirectory.Combine("pfx");
        var linuxCompatibilityDataProvider = winePrefixDirectoryPath is not null ? new LinuxCompatibilityDataProvider(gameFinderGame, winePrefixDirectoryPath.Value) : null;

        var platform = linuxCompatibilityDataProvider is null ? OSInformation.Shared.Platform : OSPlatform.Windows;

        return new GameLocatorResult
        {
            Game = game,
            Path = path,
            LocatorIds = locatorIds,
            SteamInstalledDepots = installedSteamDepots,
            Platform = platform,
            StoreIdentifier = storeIdentifier.ToString(),
            Store = Store,
            Locator = this,
        };
    }

    private class LinuxCompatibilityDataProvider : ILinuxCompatabilityDataProvider
    {
        private readonly SteamGame _steamGame;

        public AbsolutePath WinePrefixDirectoryPath { get; }

        public LinuxCompatibilityDataProvider(SteamGame steamGame, AbsolutePath winePrefixDirectoryPath)
        {
            _steamGame = steamGame;
            WinePrefixDirectoryPath = winePrefixDirectoryPath;
        }

        public ValueTask<ImmutableHashSet<string>> GetInstalledWinetricksComponents(CancellationToken cancellationToken)
        {
            var filePath = WineParser.GetWinetricksLogFilePath(WinePrefixDirectoryPath);
            var result = WineParser.ParseWinetricksLogFile(filePath);
            return new ValueTask<ImmutableHashSet<string>>(result);
        }

        public ValueTask<ImmutableArray<WineDllOverride>> GetWineDllOverrides(CancellationToken cancellationToken)
        {
            var localConfigPath = SteamLocationFinder.GetUserDataDirectoryPath(_steamGame.SteamPath, _steamGame.AppManifest.LastOwner).Combine("config").Combine("localconfig.vdf");
            var parserResult = LocalUserConfigParser.ParseConfigFile(_steamGame.AppManifest.LastOwner, localConfigPath);
            if (parserResult.IsFailed || !parserResult.Value.LocalAppData.TryGetValue(_steamGame.AppId, out var localAppData))
                return new ValueTask<ImmutableArray<WineDllOverride>>(ImmutableArray<WineDllOverride>.Empty);

            var launchOptions = localAppData.LaunchOptions;
            var section = WineParser.GetWineDllOverridesSection(launchOptions);
            var result = WineParser.ParseEnvironmentVariable(section);
            return new ValueTask<ImmutableArray<WineDllOverride>>(result);
        }
    }
}
