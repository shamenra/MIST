using System.Text;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTDeletionJournalTests
{
    [Fact]
    public async Task JournalExistsBeforeFileMoves()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            var handle = await f.BeginAsync();

            var records = MISTDeletionJournal.ReadPending(
                f.Directory);

            records.Should().ContainSingle();

            records[0].Record.InstallationEntityId
                .Should().Be("test-installation");

            records[0].Record.ReceiptEntityId
                .Should().Be("test-receipt");

            records[0].Record.ExpectedHash
                .Should().Be(f.Hash.ToHex());

            File.Exists(handle.JournalPath)
                .Should().BeTrue();

            // Merely writing the journal cannot move files.
            File.Exists(f.Original).Should().BeTrue();
            File.Exists(f.Quarantine).Should().BeFalse();
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task FinalizationAfterSimulatedCommitClearsJournal()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            var handle = await f.BeginAsync();

            await MISTFileQuarantine.MoveVerifiedAsync(
                f.Original,
                f.Quarantine,
                f.Hash);

            // Simulate a successful database commit by
            // invoking the post-commit finalization explicitly.
            await MISTDeletionJournal.FinalizeCommittedAsync(
                [handle]);

            File.Exists(f.Original).Should().BeFalse();
            File.Exists(f.Quarantine).Should().BeFalse();

            MISTDeletionJournal.ReadPending(f.Directory)
                .Should().BeEmpty();
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task FailedFinalizationPreservesJournalAndContents()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            var handle = await f.BeginAsync();

            await MISTFileQuarantine.MoveVerifiedAsync(
                f.Original,
                f.Quarantine,
                f.Hash);

            var replacement = Encoding.UTF8.GetBytes(
                "Independent user content");

            await File.WriteAllBytesAsync(
                f.Original, replacement);

            Func<Task> act = () =>
                MISTDeletionJournal.FinalizeCommittedAsync(
                    [handle]);

            await act.Should().ThrowAsync<IOException>();

            File.ReadAllBytes(f.Original)
                .Should().Equal(replacement);

            File.ReadAllBytes(f.Quarantine)
                .Should().Equal(f.Content);

            File.Exists(handle.JournalPath)
                .Should().BeTrue();
        }
        finally
        {
            f.Cleanup();
        }
    }

    [Fact]
    public async Task CorruptedJournalIsPreservedForInvestigation()
    {
        var f = await Fixture.CreateAsync();

        try
        {
            var handle = await f.BeginAsync();

            await File.WriteAllTextAsync(
                handle.JournalPath,
                "{invalid json");

            Action act = () =>
                MISTDeletionJournal.ReadPending(
                    f.Directory);

            act.Should().Throw<System.Text.Json.JsonException>();

            File.Exists(handle.JournalPath)
                .Should().BeTrue();

            File.ReadAllBytes(f.Original)
                .Should().Equal(f.Content);
        }
        finally
        {
            f.Cleanup();
        }
    }

    private sealed class Fixture
    {
        public required string Directory { get; init; }
        public required string Original { get; init; }
        public required string Quarantine { get; init; }
        public required byte[] Content { get; init; }
        public required Hash Hash { get; init; }

        public static async Task<Fixture> CreateAsync()
        {
            var directory = Path.Combine(
                Path.GetTempPath(),
                "mist-deletion-journal-" +
                Guid.NewGuid().ToString("N"));

            System.IO.Directory.CreateDirectory(directory);

            var original = Path.Combine(
                directory, "managed.archive");

            var content = Encoding.UTF8.GetBytes(
                "Verified managed file");

            await File.WriteAllBytesAsync(
                original, content);

            await using var stream =
                new MemoryStream(content);

            var hash =
                (await MultiHasher.HashStream(stream)).XxHash3;

            return new Fixture
            {
                Directory = directory,
                Original = original,
                Quarantine =
                    MISTFileQuarantine.CreateQuarantinePath(
                        original),
                Content = content,
                Hash = hash,
            };
        }

        public Task<MISTPendingDeletionHandle> BeginAsync()
        {
            return MISTDeletionJournal.BeginAsync(
                Directory,
                "test-installation",
                "test-receipt",
                Original,
                Quarantine,
                Hash);
        }

        public void Cleanup()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(
                    Directory, recursive: true);
            }
        }
    }
}
