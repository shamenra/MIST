using FluentAssertions;
using NexusMods.Abstractions.Loadouts;
using NexusMods.Games.TestFramework;
using NexusMods.MnemonicDB.Abstractions.TxFunctions;
using NexusMods.Sdk.Games;
using Xunit.Abstractions;

namespace NexusMods.DataModel.Synchronizer.Tests;

public class MISTUnmanagedFileReversalTests(
    ITestOutputHelper helper)
    : ACyberpunkIsolatedGameTest<MISTUnmanagedFileReversalTests>(
        helper)
{
    [Fact]
    public async Task UnreceiptedFileCannotBeDeletedThroughLoadout()
    {
        var loadout = await CreateLoadout();

        // Fail closed if the fixture is not using a temporary
        // installation. Never test against a real game.
        var gameRoot = Path.GetFullPath(
            GameInstallation.Locations[LocationId.Game]
                .Path.ToString());

        var tempRoot = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!gameRoot.StartsWith(tempRoot, comparison))
        {
            throw new InvalidOperationException(
                "MIST safety test requires a temporary installation.");
        }

        var externalPath = new GamePath(
            LocationId.Game,
            "mist-regression/a/b/ExternalMod.txt");

        var originalPath = new GamePath(
            LocationId.Game,
            "mist-regression/a/OriginalGame.txt");

        var userPath = new GamePath(
            LocationId.Game,
            "mist-regression/a/b/UserNotes.txt");

        var external = GameInstallation.Locations
            .ToAbsolutePath(externalPath);

        var original = GameInstallation.Locations
            .ToAbsolutePath(originalPath);

        var user = GameInstallation.Locations
            .ToAbsolutePath(userPath);

        external.Parent.CreateDirectory();

        await external.WriteAllTextAsync("external mod");
        await original.WriteAllTextAsync("original game data");
        await user.WriteAllTextAsync("user-owned notes");

        // Let the real synchronizer observe these files.
        loadout = await Synchronizer.Synchronize(loadout);

        // The file was discovered, but no deployment receipt
        // was created by MIST for it.
        var item = LoadoutItem
            .FindByLoadout(loadout.Db, loadout)
            .Single(x => x.Name == "ExternalMod.txt");

        // Request removal through the loadout database.
        // This is NOT authorization to delete the file.
        using (var tx = Connection.BeginTransaction())
        {
            tx.Delete(item.Id, false);
            await tx.Commit();
        }

        loadout = loadout.Rebase();

        Func<Task> attempt = async () =>
        {
            _ = await Synchronizer.Synchronize(loadout);
        };

        // The production mutation guard must reject deletion.
        await attempt.Should()
            .ThrowAsync<InvalidOperationException>();

        // All three files must remain byte-for-byte unchanged.
        external.FileExists.Should().BeTrue();
        original.FileExists.Should().BeTrue();
        user.FileExists.Should().BeTrue();

        (await external.ReadAllTextAsync())
            .Should().Be("external mod");

        (await original.ReadAllTextAsync())
            .Should().Be("original game data");

        (await user.ReadAllTextAsync())
            .Should().Be("user-owned notes");

        // The synchronizer must preserve ancestor directories.
        external.Parent.DirectoryExists().Should().BeTrue();
        external.Parent.Parent.DirectoryExists().Should().BeTrue();
        external.Parent.Parent.Parent.DirectoryExists().Should().BeTrue();
    }
}
