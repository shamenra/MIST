using System.Text;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Sdk.Hashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTDeploymentInspectorTests
{
    [Fact]
    public async Task MissingDestinationIsReportedWithoutChanges()
    {
        var fixture = await CreateFixture();

        try
        {
            var observations =
                await MISTDeploymentInspector.InspectAsync(
                    fixture.Directory);

            observations.Should().ContainSingle();

            observations[0].State.Should().Be(
                MISTPendingDestinationState.Absent);

            observations[0].InstallationEntityId.Should()
                .Be("mist-test-installation");

            File.Exists(fixture.Destination).Should().BeFalse();
            File.Exists(fixture.Journal).Should().BeTrue();
            File.Exists(fixture.Staged).Should().BeTrue();
        }
        finally
        {
            Cleanup(fixture.Directory);
        }
    }

    [Fact]
    public async Task MatchingBytesAreObservedButNotClaimed()
    {
        var fixture = await CreateFixture();

        try
        {
            await File.WriteAllBytesAsync(
                fixture.Destination, fixture.Content);

            var observations =
                await MISTDeploymentInspector.InspectAsync(
                    fixture.Directory);

            observations.Should().ContainSingle();

            observations[0].State.Should().Be(
                MISTPendingDestinationState.MatchesRecordedBytes);

            // Observation does not complete the journal.
            File.Exists(fixture.Journal).Should().BeTrue();
            File.Exists(fixture.Destination).Should().BeTrue();
        }
        finally
        {
            Cleanup(fixture.Directory);
        }
    }

    [Fact]
    public async Task ModifiedDestinationIsReportedAndPreserved()
    {
        var fixture = await CreateFixture();

        try
        {
            var modified = Encoding.UTF8.GetBytes(
                "Externally modified user content");

            await File.WriteAllBytesAsync(
                fixture.Destination, modified);

            var observations =
                await MISTDeploymentInspector.InspectAsync(
                    fixture.Directory);

            observations.Should().ContainSingle();

            observations[0].State.Should().Be(
                MISTPendingDestinationState.DiffersFromRecordedBytes);

            File.ReadAllBytes(fixture.Destination)
                .Should().Equal(modified);

            File.Exists(fixture.Journal).Should().BeTrue();
        }
        finally
        {
            Cleanup(fixture.Directory);
        }
    }

    [Fact]
    public async Task SymbolicLinkDestinationIsNotFollowed()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var fixture = await CreateFixture();

        try
        {
            var protectedFile = Path.Combine(
                fixture.Directory, "protected.archive");

            var content = Encoding.UTF8.GetBytes(
                "Unrelated protected content");

            await File.WriteAllBytesAsync(
                protectedFile, content);

            File.CreateSymbolicLink(
                fixture.Destination, protectedFile);

            var observations =
                await MISTDeploymentInspector.InspectAsync(
                    fixture.Directory);

            observations.Should().ContainSingle();

            observations[0].State.Should().Be(
                MISTPendingDestinationState.UnsafePath);

            File.ReadAllBytes(protectedFile)
                .Should().Equal(content);
        }
        finally
        {
            Cleanup(fixture.Directory);
        }
    }

    private static async Task<(
        string Directory,
        string Journal,
        string Staged,
        string Destination,
        byte[] Content)> CreateFixture()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "mist-inspector-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var staged = Path.Combine(directory, "staged.tmp");
        var destination = Path.Combine(directory, "mod.archive");

        var content = Encoding.UTF8.GetBytes(
            "Verified deployment fixture");

        await File.WriteAllBytesAsync(staged, content);

        await using var stream = new MemoryStream(content);
        var hash = (await MultiHasher.HashStream(stream)).XxHash3;

        var journal = await MISTDeploymentJournal.BeginAsync(
            directory,
            "mist-test-installation",
            [
                (
                    StagedPath: staged,
                    DestinationPath: destination,
                    ExpectedHash: hash
                ),
            ]);

        return (directory, journal, staged, destination, content);
    }

    private static void Cleanup(string directory)
    {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
