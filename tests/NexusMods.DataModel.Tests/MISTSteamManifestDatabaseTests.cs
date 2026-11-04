using NexusMods.Paths;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.Hashes;
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

        // Missing manifest: reject the reset.
        Action missingManifest = () =>
            SteamResetPreflight.EnsureCanReset(
                GameStore.Steam,
                "42",
                [
                    LocatorId.From("101"),
                    LocatorId.From("202"),
                    LocatorId.From("303"),
                ],
                result.Db);

        missingManifest.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Partial*");

        // Recognized but empty manifest: reject the reset.
        Action emptyManifest = () =>
            SteamResetPreflight.EnsureCanReset(
                GameStore.Steam,
                "42",
                [LocatorId.From("101")],
                result.Db);

        emptyManifest.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*no original-file records*");

        // Manifest belongs to a different Steam app.
        Action wrongApp = () =>
            SteamResetPreflight.EnsureCanReset(
                GameStore.Steam,
                "43",
                [LocatorId.From("101")],
                result.Db);

        wrongApp.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does not belong to Steam app*");

        // Steam-specific protection must not affect EGS.
        Action otherStore = () =>
            SteamResetPreflight.EnsureCanReset(
                GameStore.EGS,
                "not-a-steam-id",
                [],
                result.Db);

        otherStore.Should().NotThrow();
    }

    [Fact]
    public async Task CompleteSteamBaselinePassesPreflight()
    {
        var backend = new global::NexusMods.MnemonicDB.Storage
            .RocksDbBackend.Backend();

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

        // Synthetic content; no game installation is accessed.
        var content = System.Text.Encoding.UTF8.GetBytes(
            "MIST verified fixture");

        await using var stream = new MemoryStream(content);

        var hashes = await MultiHasher.HashStream(stream);

        using var tx = connection.BeginTransaction();

        var hashRelation = new HashRelation.New(tx)
        {
            MinimalHash = hashes.MinimalHash,
            XxHash3 = hashes.XxHash3,
            Sha1 = hashes.Sha1,
            XxHash64 = hashes.XxHash64,
            Md5 = hashes.Md5,
            Size = hashes.Size,
            Crc32 = hashes.Crc32,
        };

        var fileRelation = new PathHashRelation.New(tx)
        {
            Path = RelativePath.FromUnsanitizedInput(
                "mist-test-file.bin"),
            HashId = hashRelation,
        };

        _ = new SteamManifest.New(tx)
        {
            AppId = AppId.From(42),
            DepotId = DepotId.From(420),
            ManifestId = ManifestId.From(101),
            Name = "public",
            FilesIds = [fileRelation.Id],
        };

        var result = await tx.Commit();

        // The matching synthetic baseline should pass
        // the current preliminary preflight.
        Action act = () =>
            SteamResetPreflight.EnsureCanReset(
                GameStore.Steam,
                "42",
                [LocatorId.From("101")],
                result.Db);

        act.Should().NotThrow();
    }

}
