using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Storage;
using NexusMods.MnemonicDB.Storage.RocksDbBackend;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Hashes;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.NexusModsApi;
using Xunit;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-003B.5O: Verify that managed deployment receipts
/// can be selected and retired without affecting unrelated
/// evidence or bypassing the transaction boundary.
/// </summary>
public class MISTManagedFileReceiptRetirementTests
{
    private readonly IServiceProvider _provider;

    public MISTManagedFileReceiptRetirementTests(
        IServiceProvider provider)
    {
        _provider = provider;
    }

    [Fact]
    public async Task RetirementRemovesOnlyMatchingReceipt()
    {
        var fixture = await CreateFixtureAsync();

        var selected =
            MISTManagedFileReceiptLookup
                .FindExactlyOneMatchingReceiptId(
                    fixture.Connection.Db,
                    fixture.InstallationId,
                    fixture.Path,
                    fixture.ContentHash);

        selected.Should().Be(fixture.ReceiptId);

        if (selected is not { } receiptId)
            throw new InvalidOperationException(
                "Expected a matching receipt.");

        using var tx = fixture.Connection.BeginTransaction();

        tx.Delete(receiptId, recursive: false);

        var result = await tx.Commit();

        MISTManagedFileReceiptLookup
            .HasExactlyOneMatchingReceipt(
                result.Db,
                fixture.InstallationId,
                fixture.Path,
                fixture.ContentHash)
            .Should().BeFalse();

        // Deleting one receipt must not retire another path.
        MISTManagedFileReceiptLookup
            .HasExactlyOneMatchingReceipt(
                result.Db,
                fixture.InstallationId,
                fixture.OtherPath,
                fixture.ContentHash)
            .Should().BeTrue();
    }

    [Fact]
    public async Task AbandonedTransactionDoesNotRetireReceipt()
    {
        var fixture = await CreateFixtureAsync();

        using (var tx = fixture.Connection.BeginTransaction())
        {
            tx.Delete(
                fixture.ReceiptId,
                recursive: false);

            // Deliberately do not commit.
        }

        MISTManagedFileReceiptLookup
            .HasExactlyOneMatchingReceipt(
                fixture.Connection.Db,
                fixture.InstallationId,
                fixture.Path,
                fixture.ContentHash)
            .Should().BeTrue();
    }

    [Fact]
    public async Task WrongHashInstallationOrPathCannotSelectReceipt()
    {
        var fixture = await CreateFixtureAsync();

        MISTManagedFileReceiptLookup
            .FindExactlyOneMatchingReceiptId(
                fixture.Connection.Db,
                fixture.InstallationId,
                fixture.Path,
                Hash.Zero)
            .Should().BeNull();

        MISTManagedFileReceiptLookup
            .FindExactlyOneMatchingReceiptId(
                fixture.Connection.Db,
                fixture.OtherInstallationId,
                fixture.Path,
                fixture.ContentHash)
            .Should().BeNull();

        var missingPath = CreatePath(
            "archive/pc/mod/not-managed.archive");

        MISTManagedFileReceiptLookup
            .FindExactlyOneMatchingReceiptId(
                fixture.Connection.Db,
                fixture.InstallationId,
                missingPath,
                fixture.ContentHash)
            .Should().BeNull();

        // A rejected selection must not change the database.
        MISTManagedFileReceiptLookup
            .HasExactlyOneMatchingReceipt(
                fixture.Connection.Db,
                fixture.InstallationId,
                fixture.Path,
                fixture.ContentHash)
            .Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateReceiptsCannotSelectRetirementTarget()
    {
        var fixture = await CreateFixtureAsync(
            duplicateReceipt: true);

        MISTManagedFileReceiptLookup
            .FindExactlyOneMatchingReceiptId(
                fixture.Connection.Db,
                fixture.InstallationId,
                fixture.Path,
                fixture.ContentHash)
            .Should().BeNull();

        MISTManagedFileReceiptLookup
            .HasExactlyOneMatchingReceipt(
                fixture.Connection.Db,
                fixture.InstallationId,
                fixture.Path,
                fixture.ContentHash)
            .Should().BeFalse();

        // Unrelated receipts remain valid.
        MISTManagedFileReceiptLookup
            .HasExactlyOneMatchingReceipt(
                fixture.Connection.Db,
                fixture.InstallationId,
                fixture.OtherPath,
                fixture.ContentHash)
            .Should().BeTrue();
    }

    private async Task<Fixture> CreateFixtureAsync(
        bool duplicateReceipt = false)
    {
        var backend =
            new global::NexusMods.MnemonicDB.Storage
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
            prefix: "mist-retirement",
            queryEngine:
                _provider.GetRequiredService<IQueryEngine>());

        var content = System.Text.Encoding.UTF8.GetBytes(
            "MIST receipt retirement test content");

        await using var stream = new MemoryStream(content);

        var hash =
            (await MultiHasher.HashStream(stream)).XxHash3;

        var path = CreatePath(
            "archive/pc/mod/managed.archive");

        var otherPath = CreatePath(
            "archive/pc/mod/unrelated.archive");

        using var tx = connection.BeginTransaction();

        var installation = new GameInstallMetadata.New(tx)
        {
            Store = GameStore.ManuallyAdded,
            Path = "/mist-test/installation-a",
            GameId = NexusModsGameId.From(42),
            Name = "MIST Receipt Test A",
        };

        var otherInstallation =
            new GameInstallMetadata.New(tx)
            {
                Store = GameStore.ManuallyAdded,
                Path = "/mist-test/installation-b",
                GameId = NexusModsGameId.From(43),
                Name = "MIST Receipt Test B",
            };

        var receipt = new MISTManagedFileReceipt.New(tx)
        {
            GameInstallId = installation.Id,
            Path = path.ToGamePathParentTuple(
                installation.Id),
            DeployedHash = hash,
        };

        _ = new MISTManagedFileReceipt.New(tx)
        {
            GameInstallId = installation.Id,
            Path = otherPath.ToGamePathParentTuple(
                installation.Id),
            DeployedHash = hash,
        };

        if (duplicateReceipt)
        {
            _ = new MISTManagedFileReceipt.New(tx)
            {
                GameInstallId = installation.Id,
                Path = path.ToGamePathParentTuple(
                    installation.Id),
                DeployedHash = hash,
            };
        }

        var result = await tx.Commit();

        return new Fixture(
            connection,
            result.Remap(installation).Id,
            result.Remap(otherInstallation).Id,
            result.Remap(receipt).Id,
            path,
            otherPath,
            hash);
    }

    private static GamePath CreatePath(string relativePath)
    {
        return new GamePath(
            LocationId.Game,
            RelativePath.FromUnsanitizedInput(relativePath));
    }

    private sealed record Fixture(
        Connection Connection,
        EntityId InstallationId,
        EntityId OtherInstallationId,
        EntityId ReceiptId,
        GamePath Path,
        GamePath OtherPath,
        Hash ContentHash);
}
