using System.Collections.Immutable;
using DynamicData.Kernel;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Diagnostics.Emitters;
using NexusMods.Abstractions.Games;
using NexusMods.Abstractions.Library.Installers;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Games.Generic.Installers;
using NexusMods.Paths;
using NexusMods.Paths.Utilities;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;

namespace NexusMods.Games.OblivionRemastered;

/// <summary>
/// MIST-006-03A: Initial, deliberately limited support
/// for the Steam edition of Oblivion Remastered.
///
/// Only the Unreal PAK-mod directory is managed.
/// ESP/ESM ordering, UE4SS and OBSE are not implemented.
/// </summary>
public class OblivionRemasteredGame :
    IGame,
    IGameData<OblivionRemasteredGame>
{
    public const uint SteamAppId = 2623190u;

    public static GameId GameId { get; } =
        GameId.From("MIST.OblivionRemastered");

    public static string DisplayName =>
        "The Elder Scrolls IV: Oblivion Remastered";

    public static Optional<Sdk.NexusModsApi.NexusModsGameId>
        NexusModsGameId =>
        Sdk.NexusModsApi.NexusModsGameId.From(7587);

    public StoreIdentifiers StoreIdentifiers { get; } =
        new(GameId)
        {
            SteamAppIds = [SteamAppId],
        };

    public static GamePath PakModsPath { get; } = new(
        LocationId.Game,
        "OblivionRemastered/Content/Paks/~mods");

    public IStreamFactory IconImage { get; } =
        new EmbeddedResourceStreamFactory<OblivionRemasteredGame>(
            "NexusMods.Games.OblivionRemastered.Resources.OblivionRemastered.thumbnail.png");

    public IStreamFactory TileImage { get; } =
        new EmbeddedResourceStreamFactory<OblivionRemasteredGame>(
            "NexusMods.Games.OblivionRemastered.Resources.OblivionRemastered.tile.png");

    private readonly Lazy<ILoadoutSynchronizer> _synchronizer;

    public ILoadoutSynchronizer Synchronizer =>
        _synchronizer.Value;

    private readonly Lazy<ISortOrderManager> _sortOrderManager;

    public ISortOrderManager SortOrderManager =>
        _sortOrderManager.Value;

    public IDiagnosticEmitter[] DiagnosticEmitters { get; } = [];

    public ILibraryItemInstaller[] LibraryItemInstallers { get; }

    public OblivionRemasteredGame(IServiceProvider provider)
    {
        _synchronizer =
            new Lazy<ILoadoutSynchronizer>(
                () => new OblivionRemasteredSynchronizer(provider));

        _sortOrderManager =
            new Lazy<ISortOrderManager>(() =>
            {
                var manager =
                    provider.GetRequiredService<SortOrderManager>();

                manager.RegisterSortOrderVarieties([], this);

                return manager;
            });

        LibraryItemInstallers =
        [
            new GenericPatternMatchInstaller(provider)
            {
                InstallFolderTargets =
                [
                    new InstallFolderTarget
                    {
                        DestinationGamePath = PakModsPath,

                        // The .mistcheck extension is reserved
                        // for our harmless deployment smoke test.
                        // It is NOT a real game mod format.
                        KnownValidFileExtensions =
                        [
                            new Extension(".pak"),
                            new Extension(".ucas"),
                            new Extension(".utoc"),
                            new Extension(".mistcheck"),
                        ],

                        FileExtensionsToDiscard =
                        [
                            KnownExtensions.Txt,
                            KnownExtensions.Md,
                            KnownExtensions.Pdf,
                            KnownExtensions.Png,
                            new Extension(".lnk"),
                        ],
                    },
                ],
            },
        ];
    }

    public ImmutableDictionary<LocationId, AbsolutePath>
        GetLocations(
            IFileSystem fileSystem,
            GameLocatorResult gameLocatorResult)
    {
        return new Dictionary<LocationId, AbsolutePath>
        {
            { LocationId.Game, gameLocatorResult.Path },
        }.ToImmutableDictionary();
    }

    public GamePath GetPrimaryFile(
        GameInstallation installation)
    {
        return new GamePath(
            LocationId.Game,
            "OblivionRemastered.exe");
    }
}
