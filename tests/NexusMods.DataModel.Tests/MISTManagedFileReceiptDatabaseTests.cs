using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.MnemonicDB.Storage;
using NexusMods.MnemonicDB.Storage.RocksDbBackend;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.IO;
using NexusMods.Sdk.Hashes;
using NexusMods.Sdk.NexusModsApi;
using Xunit;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-003B.3: Verify managed deployment receipts survive
/// a database transaction and retain their installation,
/// destination, and deployed-content hash.
/// </summary>
public class MISTManagedFileReceiptDatabaseTests
{
    private readonly IServiceProvider _provider;

    public MISTManagedFileReceiptDatabaseTests(IServiceProvider provider)
    {
        _provider = provider;
    }

    [Fact]
    public async Task ManagedFileReceiptSurvivesDatabaseRoundTrip()
    {
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
            prefix: "mist-receipts",
            queryEngine: _provider.GetRequiredService<IQueryEngine>());

        // Synthetic file content. Nothing is written to a game.
        var content = System.Text.Encoding.UTF8.GetBytes(
            "MIST managed deployment receipt fixture");

        await using var stream = new MemoryStream(content);
        var hashes = await MultiHasher.HashStream(stream);

        using var tx = connection.BeginTransaction();

        // Create a synthetic installation in the same transaction.
        var installation = new GameInstallMetadata.New(tx)
        {
            Store = GameStore.ManuallyAdded,
            Path = "/mist-test/game",
            GameId = NexusModsGameId.From(42),
            Name = "MIST Receipt Test",
        };

        var expectedPath = new GamePath(
            LocationId.Game,
            RelativePath.FromUnsanitizedInput(
                "archive/pc/mod/mist-test.archive"));

        // The receipt explicitly references the installation.
        var receipt = new MISTManagedFileReceipt.New(tx)
        {
            GameInstallId = installation.Id,
            Path = expectedPath.ToGamePathParentTuple(
                installation.Id),
            DeployedHash = hashes.XxHash3,
        };

        var result = await tx.Commit();

        var savedInstallation = result.Remap(installation);
        var savedReceipt = result.Remap(receipt);

        // Reload from the committed database rather than
        // merely checking the values we assigned to New().
        var loaded = MISTManagedFileReceipt.Load(
            result.Db,
            savedReceipt.Id);

        loaded.GameInstallId.Value.Should().Be(
            savedInstallation.Id);

        loaded.Path.Should().Be(
            expectedPath.ToGamePathParentTuple(savedInstallation.Id));

        loaded.DeployedHash.Should().Be(
            hashes.XxHash3);

        // A valid receipt matches the installation, path,
        // and observed content hash.
        MISTManagedFileReceiptLookup.HasExactlyOneMatchingReceipt(
            result.Db,
            savedInstallation.Id,
            expectedPath,
            hashes.XxHash3)
            .Should().BeTrue();

        // A receipt must not authorize modification if the
        // observed file contents no longer match.
        MISTManagedFileReceiptLookup.HasExactlyOneMatchingReceipt(
            result.Db,
            savedInstallation.Id,
            expectedPath,
            NexusMods.Hashing.xxHash3.Hash.Zero)
            .Should().BeFalse();

        // A receipt for one destination cannot authorize
        // changes to another destination.
        var unrelatedPath = new GamePath(
            LocationId.Game,
            RelativePath.FromUnsanitizedInput(
                "archive/pc/mod/unmanaged.archive"));

        MISTManagedFileReceiptLookup.HasExactlyOneMatchingReceipt(
            result.Db,
            savedInstallation.Id,
            unrelatedPath,
            hashes.XxHash3)
            .Should().BeFalse();

        // Ambiguous deployment history is not authorization.
        using var duplicateTx = connection.BeginTransaction();

        _ = new MISTManagedFileReceipt.New(duplicateTx)
        {
            GameInstallId = savedInstallation.Id,
            Path = expectedPath.ToGamePathParentTuple(
                savedInstallation.Id),
            DeployedHash = hashes.XxHash3,
        };

        var duplicateResult = await duplicateTx.Commit();

        MISTManagedFileReceiptLookup.HasExactlyOneMatchingReceipt(
            duplicateResult.Db,
            savedInstallation.Id,
            expectedPath,
            hashes.XxHash3)
            .Should().BeFalse();
    }
}
