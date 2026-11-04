using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Games.FileHashes;
using NexusMods.Abstractions.Games.FileHashes.Models;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.MnemonicDB;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Storage;
using NexusMods.MnemonicDB.Storage.RocksDbBackend;
using NexusMods.Sdk.Games;
using Xunit;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-003: Verify manifest coverage using actual MnemonicDB
/// records rather than simulated lookup callbacks.
/// </summary>
public class MISTSteamManifestDatabaseTests
{
    private readonly IServiceProvider _provider;

    public MISTSteamManifestDatabaseTests(IServiceProvider provider)
    {
        _provider = provider;
    }

    [Fact]
    public async Task DatabaseLookupDetectsMissingSteamManifest()
    {
        // Create an isolated in-memory database.
        var backend = new global::NexusMods.MnemonicDB.Storage.RocksDbBackend.Backend();

        var store = new DatomStore(
            _provider.GetRequiredService<ILogger<DatomStore>>(),
            DatomStoreSettings.InMemory,
            backend);

        var connection = new Connection(
            _provider.GetRequiredService<ILogger<Connection>>(),
            store,
            _provider,
            [],
            prefix: "hashes",
            queryEngine: _provider.GetRequiredService<IQueryEngine>());

        // Insert two synthetic Steam manifest records.
        using var tx = connection.BeginTransaction();

        _ = new SteamManifest.New(tx)
        {
            AppId = AppId.From(42),
            DepotId = DepotId.From(420),
            ManifestId = ManifestId.From(101),
            Name = "public",
            FilesIds = [],
        };

        _ = new SteamManifest.New(tx)
        {
            AppId = AppId.From(42),
            DepotId = DepotId.From(430),
            ManifestId = ManifestId.From(303),
            Name = "public",
            FilesIds = [],
        };

        var result = await tx.Commit();

        // Three installed manifests, only two known.
        var coverage =
            SteamManifestCoverageEvaluator.EvaluateDatabase(
                new[]
                {
                    LocatorId.From("101"),
                    LocatorId.From("202"),
                    LocatorId.From("303"),
                },
                result.Db);

        coverage.Status.Should().Be(
            SteamManifestCoverageStatus.Partial);

        coverage.ManifestCount.Should().Be(3);
        coverage.KnownCount.Should().Be(2);

        coverage.MissingManifestIds.Should()
            .Equal(202UL);

        coverage.AllInstalledManifestsKnown.Should()
            .BeFalse();
    }
}
