using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Hashing.xxHash3;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTPendingOperationGateTests
{
    private const string Installation = "mist-test-installation-A";
    private const string OtherInstallation = "mist-test-installation-B";

    [Fact]
    public void MissingJournalDirectoryIsAllowedWithoutCreatingIt()
    {
        using var temp = new TestDirectory();
        var missing = temp.PathFor("not-created");

        var assessment = MISTPendingOperationGate.Assess(missing, Installation);
        Assert.Equal(0, assessment.Total);
        Assert.False(assessment.RequiresReview);

        MISTPendingOperationGate.EnsureClear(missing, Installation);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public async Task PendingDeploymentForSameInstallationBlocksAndIsPreserved()
    {
        using var temp = new TestDirectory();
        var journal = temp.PathFor("journal");
        var pending = await AddDeployment(temp, journal, Installation);

        var assessment = MISTPendingOperationGate.Assess(journal, Installation);
        Assert.Equal(1, assessment.Deployments);
        Assert.Equal(0, assessment.Deletions);
        Assert.True(assessment.RequiresReview);

        var error = Assert.Throws<InvalidOperationException>(
            () => MISTPendingOperationGate.EnsureClear(journal, Installation));
        Assert.Contains("unresolved deployment", error.Message);
        Assert.True(File.Exists(pending));
        Assert.False(File.Exists(temp.PathFor("destination.archive")));
    }

    [Fact]
    public async Task PendingDeletionForSameInstallationAlsoBlocks()
    {
        using var temp = new TestDirectory();
        var journal = temp.PathFor("journal");
        var original = temp.PathFor("original.archive");
        var quarantine = temp.PathFor(".mist-quarantine-test");
        await File.WriteAllTextAsync(original, "User content remains here");

        var pending = await MISTDeletionJournal.BeginAsync(
            journal,
            Installation,
            "mist-test-receipt",
            original,
            quarantine,
            Hash.From(0xBAD1));

        var assessment = MISTPendingOperationGate.Assess(journal, Installation);
        Assert.Equal(0, assessment.Deployments);
        Assert.Equal(1, assessment.Deletions);

        Assert.Throws<InvalidOperationException>(
            () => MISTPendingOperationGate.EnsureClear(journal, Installation));
        Assert.True(File.Exists(pending.JournalPath));
        Assert.Equal("User content remains here", await File.ReadAllTextAsync(original));
        Assert.False(File.Exists(quarantine));
    }

    [Fact]
    public async Task JournalsFromOtherInstallationDoNotBlockThisInstallation()
    {
        using var temp = new TestDirectory();
        var journal = temp.PathFor("journal");
        var pending = await AddDeployment(temp, journal, OtherInstallation);

        MISTPendingOperationGate.EnsureClear(journal, Installation);
        Assert.Equal(0, MISTPendingOperationGate.Assess(journal, Installation).Total);
        Assert.True(File.Exists(pending));
    }

    [Fact]
    public async Task AlternateInstallationIdentifierAlsoBlocks()
    {
        using var temp = new TestDirectory();
        var journal = temp.PathFor("journal");
        var pending = await AddDeployment(temp, journal, OtherInstallation);

        // Supports both typed installation-id and underlying EntityId
        // string representations used by older journal-writing paths.
        var assessment = MISTPendingOperationGate.Assess(
            journal, Installation, OtherInstallation);
        Assert.Equal(1, assessment.Total);
        Assert.Throws<InvalidOperationException>(() =>
            MISTPendingOperationGate.EnsureClear(
                journal, Installation, OtherInstallation));
        Assert.True(File.Exists(pending));
    }

    [Fact]
    public async Task MixedPendingOperationsAreCountedAndBothPreserved()
    {
        using var temp = new TestDirectory();
        var journal = temp.PathFor("journal");
        var deployment = await AddDeployment(temp, journal, Installation);
        var deletion = await MISTDeletionJournal.BeginAsync(
            journal,
            Installation,
            "mist-test-receipt",
            temp.PathFor("original.archive"),
            temp.PathFor(".mist-quarantine-test"),
            Hash.From(0xBAD1));

        var assessment = MISTPendingOperationGate.Assess(journal, Installation);
        Assert.Equal(2, assessment.Total);
        Assert.Equal(1, assessment.Deployments);
        Assert.Equal(1, assessment.Deletions);
        Assert.Throws<InvalidOperationException>(
            () => MISTPendingOperationGate.EnsureClear(journal, Installation));
        Assert.True(File.Exists(deployment));
        Assert.True(File.Exists(deletion.JournalPath));
    }

    [Fact]
    public void MalformedPendingDeploymentFailsClosed()
    {
        using var temp = new TestDirectory();
        var journal = temp.PathFor("journal");
        Directory.CreateDirectory(journal);
        var invalid = Path.Combine(journal, "bad.pending.json");
        File.WriteAllText(invalid, "{invalid json");

        Assert.Throws<System.Text.Json.JsonException>(
            () => MISTPendingOperationGate.EnsureClear(journal, Installation));
        Assert.True(File.Exists(invalid));
    }

    private static Task<string> AddDeployment(
        TestDirectory temp,
        string journalDirectory,
        string installationEntityId) =>
        MISTDeploymentJournal.BeginAsync(
            journalDirectory,
            installationEntityId,
            [
                (
                    StagedPath: temp.PathFor("stage.archive"),
                    DestinationPath: temp.PathFor("destination.archive"),
                    ExpectedHash: Hash.From(0xC0FFEE)
                ),
            ]);

    private sealed class TestDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "mist-pending-operation-gate-" + Guid.NewGuid().ToString("N"));

        public TestDirectory() => Directory.CreateDirectory(_root);
        public string PathFor(string name) => Path.Combine(_root, name);

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
    }
}
