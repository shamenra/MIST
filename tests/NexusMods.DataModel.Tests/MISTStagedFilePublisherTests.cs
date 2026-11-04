using System.Text;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTStagedFilePublisherTests
{
    private static async Task<Hash> HashContent(byte[] content)
    {
        await using var stream = new MemoryStream(content);
        var hashes = await MultiHasher.HashStream(stream);
        return hashes.XxHash3;
    }

    [Fact]
    public async Task PublishesVerifiedNewFile()
    {
        var directory = CreateTestDirectory();

        try
        {
            var staged = Path.Combine(directory, "staged.tmp");
            var destination = Path.Combine(directory, "mod.archive");

            var content = Encoding.UTF8.GetBytes("MIST managed mod");
            await File.WriteAllBytesAsync(staged, content);

            var hash = await HashContent(content);

            await MISTStagedFilePublisher.PublishNewFileAsync(
                staged, destination, hash);

            File.Exists(destination).Should().BeTrue();
            File.Exists(staged).Should().BeFalse();

            File.ReadAllBytes(destination).Should().Equal(content);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RefusesToOverwriteExistingFile()
    {
        var directory = CreateTestDirectory();

        try
        {
            var staged = Path.Combine(directory, "staged.tmp");
            var destination = Path.Combine(directory, "mod.archive");

            var newContent = Encoding.UTF8.GetBytes("New mod");
            var original = Encoding.UTF8.GetBytes("Existing user file");

            await File.WriteAllBytesAsync(staged, newContent);
            await File.WriteAllBytesAsync(destination, original);

            var hash = await HashContent(newContent);

            Func<Task> act = () =>
                MISTStagedFilePublisher.PublishNewFileAsync(
                    staged, destination, hash);

            await act.Should().ThrowAsync<IOException>();

            File.ReadAllBytes(destination).Should().Equal(original);
            File.Exists(staged).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsIncorrectStagedHash()
    {
        var directory = CreateTestDirectory();

        try
        {
            var staged = Path.Combine(directory, "staged.tmp");
            var destination = Path.Combine(directory, "mod.archive");

            var expected = Encoding.UTF8.GetBytes("Expected content");
            var actual = Encoding.UTF8.GetBytes("Corrupted content");

            await File.WriteAllBytesAsync(staged, actual);

            var expectedHash = await HashContent(expected);

            Func<Task> act = () =>
                MISTStagedFilePublisher.PublishNewFileAsync(
                    staged, destination, expectedHash);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*hash mismatch*");

            File.Exists(destination).Should().BeFalse();
            File.Exists(staged).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchRejectsLaterCorruptionBeforePublishingAnyFile()
    {
        var directory = CreateTestDirectory();

        try
        {
            var stagedA = Path.Combine(directory, "a.tmp");
            var stagedB = Path.Combine(directory, "b.tmp");

            var destinationA = Path.Combine(directory, "a.archive");
            var destinationB = Path.Combine(directory, "b.archive");

            var contentA = Encoding.UTF8.GetBytes("Valid first mod");
            var expectedB = Encoding.UTF8.GetBytes("Expected second mod");
            var actualB = Encoding.UTF8.GetBytes("Corrupted second mod");

            await File.WriteAllBytesAsync(stagedA, contentA);
            await File.WriteAllBytesAsync(stagedB, actualB);

            var batch = new[]
            {
                (
                    StagedPath: stagedA,
                    DestinationPath: destinationA,
                    ExpectedHash: await HashContent(contentA)
                ),
                (
                    StagedPath: stagedB,
                    DestinationPath: destinationB,
                    ExpectedHash: await HashContent(expectedB)
                ),
            };

            Func<Task> act = () =>
                MISTStagedFilePublisher.PublishNewFilesAsync(batch);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*hash mismatch*");

            File.Exists(destinationA).Should().BeFalse();
            File.Exists(destinationB).Should().BeFalse();

            File.Exists(stagedA).Should().BeTrue();
            File.Exists(stagedB).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchRejectsLaterDestinationConflictBeforePublishing()
    {
        var directory = CreateTestDirectory();

        try
        {
            var stagedA = Path.Combine(directory, "a.tmp");
            var stagedB = Path.Combine(directory, "b.tmp");

            var destinationA = Path.Combine(directory, "a.archive");
            var destinationB = Path.Combine(directory, "b.archive");

            var contentA = Encoding.UTF8.GetBytes("First mod");
            var contentB = Encoding.UTF8.GetBytes("Second mod");
            var existing = Encoding.UTF8.GetBytes("User file");

            await File.WriteAllBytesAsync(stagedA, contentA);
            await File.WriteAllBytesAsync(stagedB, contentB);
            await File.WriteAllBytesAsync(destinationB, existing);

            var batch = new[]
            {
                (
                    StagedPath: stagedA,
                    DestinationPath: destinationA,
                    ExpectedHash: await HashContent(contentA)
                ),
                (
                    StagedPath: stagedB,
                    DestinationPath: destinationB,
                    ExpectedHash: await HashContent(contentB)
                ),
            };

            Func<Task> act = () =>
                MISTStagedFilePublisher.PublishNewFilesAsync(batch);

            await act.Should().ThrowAsync<IOException>();

            File.Exists(destinationA).Should().BeFalse();

            File.ReadAllBytes(destinationB)
                .Should().Equal(existing);

            File.Exists(stagedA).Should().BeTrue();
            File.Exists(stagedB).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BatchPublishesEveryVerifiedNewFile()
    {
        var directory = CreateTestDirectory();

        try
        {
            var stagedA = Path.Combine(directory, "a.tmp");
            var stagedB = Path.Combine(directory, "b.tmp");

            var destinationA = Path.Combine(directory, "a.archive");
            var destinationB = Path.Combine(directory, "b.archive");

            var contentA = Encoding.UTF8.GetBytes("First verified mod");
            var contentB = Encoding.UTF8.GetBytes("Second verified mod");

            await File.WriteAllBytesAsync(stagedA, contentA);
            await File.WriteAllBytesAsync(stagedB, contentB);

            var batch = new[]
            {
                (
                    StagedPath: stagedA,
                    DestinationPath: destinationA,
                    ExpectedHash: await HashContent(contentA)
                ),
                (
                    StagedPath: stagedB,
                    DestinationPath: destinationB,
                    ExpectedHash: await HashContent(contentB)
                ),
            };

            await MISTStagedFilePublisher.PublishNewFilesAsync(batch);

            File.ReadAllBytes(destinationA).Should().Equal(contentA);
            File.ReadAllBytes(destinationB).Should().Equal(contentB);

            File.Exists(stagedA).Should().BeFalse();
            File.Exists(stagedB).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledBatchRecoversPublishedFiles()
    {
        var directory = CreateTestDirectory();

        try
        {
            var stagedA = Path.Combine(directory, "a.tmp");
            var stagedB = Path.Combine(directory, "b.tmp");

            var destinationA = Path.Combine(
                directory, "a.archive");

            var destinationB = Path.Combine(
                directory, "b.archive");

            var contentA = Encoding.UTF8.GetBytes("First mod");
            var contentB = Encoding.UTF8.GetBytes("Second mod");

            await File.WriteAllBytesAsync(stagedA, contentA);
            await File.WriteAllBytesAsync(stagedB, contentB);

            var batch = new[]
            {
                (
                    StagedPath: stagedA,
                    DestinationPath: destinationA,
                    ExpectedHash: await HashContent(contentA)
                ),
                (
                    StagedPath: stagedB,
                    DestinationPath: destinationB,
                    ExpectedHash: await HashContent(contentB)
                ),
            };

            using var cancellation = new CancellationTokenSource();

            Action<(int Current, int Max)> progress = value =>
            {
                if (value.Current == 1)
                    cancellation.Cancel();
            };

            Func<Task> act = () =>
                MISTStagedFilePublisher.PublishNewFilesAsync(
                    batch,
                    cancellation.Token,
                    progress);

            await act.Should()
                .ThrowAsync<OperationCanceledException>();

            // The first published file was recovered.
            File.Exists(destinationA).Should().BeFalse();
            File.Exists(destinationB).Should().BeFalse();

            File.ReadAllBytes(stagedA)
                .Should().Equal(contentA);

            File.ReadAllBytes(stagedB)
                .Should().Equal(contentB);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecoveryPreservesExternallyModifiedFile()
    {
        var directory = CreateTestDirectory();

        try
        {
            var staged = Path.Combine(directory, "mod.tmp");
            var destination = Path.Combine(
                directory, "mod.archive");

            var original = Encoding.UTF8.GetBytes(
                "MIST deployed content");

            var modified = Encoding.UTF8.GetBytes(
                "Externally modified content");

            await File.WriteAllBytesAsync(staged, original);

            var batch = new[]
            {
                (
                    StagedPath: staged,
                    DestinationPath: destination,
                    ExpectedHash: await HashContent(original)
                ),
            };

            // Simulate an external modification followed by
            // an error while reporting publication progress.
            Action<(int Current, int Max)> progress = _ =>
            {
                File.WriteAllBytes(destination, modified);

                throw new IOException(
                    "Simulated publication interruption");
            };

            Func<Task> act = () =>
                MISTStagedFilePublisher.PublishNewFilesAsync(
                    batch,
                    progressUpdater: progress);

            await act.Should()
                .ThrowAsync<AggregateException>()
                .WithMessage("*manual review required*");

            // M.I.S.T. must preserve changed content.
            File.ReadAllBytes(destination)
                .Should().Equal(modified);

            File.Exists(staged).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "mist-deployment-test-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        return directory;
    }
}
