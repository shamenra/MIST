using System.Text;
using System.Text.Json;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTDeploymentJournalTests
{
    private static async Task<Hash> HashContent(
        byte[] content)
    {
        await using var stream = new MemoryStream(content);

        return (await MultiHasher.HashStream(stream))
            .XxHash3;
    }

    [Fact]
    public async Task PendingDeploymentSurvivesDiskRoundTrip()
    {
        var directory = CreateDirectory();

        try
        {
            var staged = Path.Combine(
                directory, "staged.tmp");

            var destination = Path.Combine(
                directory, "mod.archive");

            var content = Encoding.UTF8.GetBytes(
                "MIST journal fixture");

            await File.WriteAllBytesAsync(
                staged, content);

            var hash = await HashContent(content);

            var pending = await MISTDeploymentJournal
                .BeginAsync(
                    directory,
                    "mist-test-installation",
                    [
                        (
                            StagedPath: staged,
                            DestinationPath: destination,
                            ExpectedHash: hash
                        ),
                    ]);

            File.Exists(pending).Should().BeTrue();

            var records =
                MISTDeploymentJournal.ReadPending(
                    directory);

            records.Should().ContainSingle();

            var record = records.Single().Record;

            record.OperationId.Should()
                .NotBe(Guid.Empty);

            record.InstallationEntityId.Should()
                .Be("mist-test-installation");

            record.Files.Should().ContainSingle();

            record.Files[0].StagedPath
                .Should().Be(staged);

            record.Files[0].DestinationPath
                .Should().Be(destination);

            record.Files[0].ExpectedHash
                .Should().Be(hash.ToHex());

            // Journal creation does not publish files.
            File.Exists(destination).Should().BeFalse();

            File.ReadAllBytes(staged)
                .Should().Equal(content);
        }
        finally
        {
            Directory.Delete(
                directory, recursive: true);
        }
    }

    [Fact]
    public async Task CompletionRemovesPendingRecord()
    {
        var directory = CreateDirectory();

        try
        {
            var pending = await CreatePending(
                directory);

            MISTDeploymentJournal.ReadPending(directory)
                .Should().ContainSingle();

            // Simulates the caller confirming that its
            // separate database transaction committed.
            MISTDeploymentJournal.Complete(pending);

            File.Exists(pending).Should().BeFalse();

            MISTDeploymentJournal.ReadPending(directory)
                .Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(
                directory, recursive: true);
        }
    }

    [Fact]
    public async Task CorruptedJournalIsNotSilentlyDiscarded()
    {
        var directory = CreateDirectory();

        try
        {
            var pending = await CreatePending(
                directory);

            await File.WriteAllTextAsync(
                pending, "{invalid json");

            Action act = () =>
                MISTDeploymentJournal.ReadPending(
                    directory);

            act.Should().Throw<JsonException>();

            // A malformed record must remain available
            // for investigation, not disappear silently.
            File.Exists(pending).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(
                directory, recursive: true);
        }
    }

    private static async Task<string> CreatePending(
        string directory)
    {
        var content = Encoding.UTF8.GetBytes(
            "Synthetic deployment");

        var hash = await HashContent(content);

        return await MISTDeploymentJournal.BeginAsync(
            directory,
            "mist-test-installation",
            [
                (
                    StagedPath: Path.Combine(
                        directory, "staged.tmp"),
                    DestinationPath: Path.Combine(
                        directory, "mod.archive"),
                    ExpectedHash: hash
                ),
            ]);
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "mist-journal-" +
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        return directory;
    }
}
