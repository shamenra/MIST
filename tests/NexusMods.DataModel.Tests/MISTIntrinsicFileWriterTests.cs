using System.Text;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTIntrinsicFileWriterTests
{
    private const string InstallationId = "mist-intrinsic-test-installation";

    [Fact]
    public async Task ExistingDestinationIsNeverTruncated()
    {
        using var temp = new TestDirectory();
        var destination = temp.PathFor("existing.txt");
        var journalDir = temp.PathFor("journal");
        var original = Encoding.UTF8.GetBytes("Original game configuration");
        await File.WriteAllBytesAsync(destination, original);

        var writerCalled = false;

        await Assert.ThrowsAnyAsync<IOException>(() =>
            MISTIntrinsicFileWriter.WriteJournaledNewFileAsync(
                destination,
                async stream =>
                {
                    writerCalled = true;
                    await stream.WriteAsync(
                        Encoding.UTF8.GetBytes("Replacement"));
                },
                journalDir,
                InstallationId));

        // The new writer may run, but the destination is never replaced.
        Assert.True(writerCalled);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Single(MISTDeploymentJournal.ReadPending(journalDir));
    }

    [Fact]
    public async Task NewDestinationCanBeCreatedAndJournalsBeforeCommit()
    {
        using var temp = new TestDirectory();
        var destination = temp.PathFor("new.txt");
        var journalDir = temp.PathFor("journal");
        var expected = Encoding.UTF8.GetBytes("New intrinsic content");

        var pending = await MISTIntrinsicFileWriter
            .WriteJournaledNewFileAsync(
                destination,
                stream => stream.WriteAsync(expected).AsTask(),
                journalDir,
                InstallationId);

        Assert.Equal(expected, await File.ReadAllBytesAsync(destination));
        Assert.True(File.Exists(pending));

        var (journalPath, record) =
            Assert.Single(MISTDeploymentJournal.ReadPending(journalDir));
        Assert.Equal(pending, journalPath);
        Assert.Equal(InstallationId, record.InstallationEntityId);
        var file = Assert.Single(record.Files);
        Assert.Equal(destination, file.DestinationPath);

        await using var stream = File.OpenRead(destination);
        var hash = (await MultiHasher.HashStream(stream)).XxHash3;
        Assert.Equal(hash.ToHex(), file.ExpectedHash);
    }

    [Fact]
    public async Task SymlinkDestinationCannotReplaceTarget()
    {
        if (!OperatingSystem.IsLinux())
            return;

        using var temp = new TestDirectory();
        var target = temp.PathFor("protected.txt");
        var link = temp.PathFor("linked.txt");
        var journalDir = temp.PathFor("journal");
        var original = Encoding.UTF8.GetBytes("Protected content");
        await File.WriteAllBytesAsync(target, original);
        File.CreateSymbolicLink(link, target);

        await Assert.ThrowsAnyAsync<IOException>(() =>
            MISTIntrinsicFileWriter.WriteJournaledNewFileAsync(
                link,
                stream => stream.WriteAsync(
                    Encoding.UTF8.GetBytes("Replacement")).AsTask(),
                journalDir,
                InstallationId));

        Assert.Equal(original, await File.ReadAllBytesAsync(target));
        Assert.Single(MISTDeploymentJournal.ReadPending(journalDir));
    }

    [Fact]
    public async Task FailedGenerationDoesNotPublishPartialDestination()
    {
        using var temp = new TestDirectory();
        var destination = temp.PathFor("failed.txt");
        var journalDir = temp.PathFor("journal");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MISTIntrinsicFileWriter.WriteJournaledNewFileAsync(
                destination,
                async stream =>
                {
                    await stream.WriteAsync(
                        Encoding.UTF8.GetBytes("Incomplete content"));
                    throw new InvalidOperationException("Synthetic write failure");
                },
                journalDir,
                InstallationId));

        Assert.False(File.Exists(destination));
        Assert.Empty(Directory.GetFiles(
            Path.GetDirectoryName(destination)!, ".mist-stage-*"));
        Assert.Empty(MISTDeploymentJournal.ReadPending(journalDir));
    }

    [Fact]
    public async Task DestinationAppearingDuringGenerationIsNotOverwrittenAndJournalRemains()
    {
        using var temp = new TestDirectory();
        var destination = temp.PathFor("collision.txt");
        var journalDir = temp.PathFor("journal");
        var existing = Encoding.UTF8.GetBytes("Independent user's file");
        var stagedContent = Encoding.UTF8.GetBytes("Generated intrinsic file");

        await Assert.ThrowsAnyAsync<IOException>(() =>
            MISTIntrinsicFileWriter.WriteJournaledNewFileAsync(
                destination,
                async stream =>
                {
                    await stream.WriteAsync(stagedContent);
                    // Simulate another process creating the destination
                    // between the initial guard and publication.
                    await File.WriteAllBytesAsync(destination, existing);
                },
                journalDir,
                InstallationId));

        Assert.Equal(existing, await File.ReadAllBytesAsync(destination));
        var (_, record) = Assert.Single(
            MISTDeploymentJournal.ReadPending(journalDir));
        var file = Assert.Single(record.Files);
        Assert.Equal(destination, file.DestinationPath);
        Assert.True(File.Exists(file.StagedPath));
        Assert.Equal(stagedContent, await File.ReadAllBytesAsync(file.StagedPath));
    }

    private sealed class TestDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "mist-intrinsic-" + Guid.NewGuid().ToString("N"));

        public TestDirectory()
        {
            Directory.CreateDirectory(_root);
        }

        public string PathFor(string name) => Path.Combine(_root, name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
