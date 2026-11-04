using FluentAssertions;
using NexusMods.Abstractions.Games.FileHashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTSteamManifestCoverageTests
{
    [Fact]
    public void RecognizesCompleteIdentifierCoverage()
    {
        var coverage = SteamManifestCoverageEvaluator.Evaluate(
            new[] { "101", "202", "303" },
            id => id is 101 or 202 or 303);

        coverage.Status.Should().Be(
            SteamManifestCoverageStatus.AllKnown);

        coverage.ManifestCount.Should().Be(3);
        coverage.KnownCount.Should().Be(3);
        coverage.MissingManifestIds.Should().BeEmpty();
        coverage.AllInstalledManifestsKnown.Should().BeTrue();
    }

    [Fact]
    public void DetectsPartialCoverage()
    {
        var coverage = SteamManifestCoverageEvaluator.Evaluate(
            new[] { "101", "202", "303" },
            id => id is 101 or 303);

        coverage.Status.Should().Be(
            SteamManifestCoverageStatus.Partial);

        coverage.ManifestCount.Should().Be(3);
        coverage.KnownCount.Should().Be(2);
        coverage.MissingManifestIds.Should().Equal(202UL);
        coverage.AllInstalledManifestsKnown.Should().BeFalse();
    }

    [Fact]
    public void DetectsCompletelyUnknownManifests()
    {
        var coverage = SteamManifestCoverageEvaluator.Evaluate(
            new[] { "101", "202" },
            _ => false);

        coverage.Status.Should().Be(
            SteamManifestCoverageStatus.NoneKnown);

        coverage.KnownCount.Should().Be(0);
        coverage.MissingManifestIds.Should().Equal(
            101UL, 202UL);
    }

    [Fact]
    public void MissingMetadataIsNotComplete()
    {
        var coverage = SteamManifestCoverageEvaluator.Evaluate(
            Array.Empty<string>(),
            _ => true);

        coverage.Status.Should().Be(
            SteamManifestCoverageStatus.MissingMetadata);

        coverage.AllInstalledManifestsKnown.Should().BeFalse();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0")]
    [InlineData("-1")]
    public void InvalidIdentifiersAreNotAccepted(string identifier)
    {
        var coverage = SteamManifestCoverageEvaluator.Evaluate(
            new[] { "101", identifier },
            _ => true);

        coverage.Status.Should().Be(
            SteamManifestCoverageStatus.InvalidMetadata);

        coverage.AllInstalledManifestsKnown.Should().BeFalse();
    }

    [Fact]
    public void DuplicateManifestIdsAreCountedOnce()
    {
        var coverage = SteamManifestCoverageEvaluator.Evaluate(
            new[] { "101", "101", "202" },
            id => id is 101 or 202);

        coverage.Status.Should().Be(
            SteamManifestCoverageStatus.AllKnown);

        coverage.ManifestCount.Should().Be(2);
        coverage.KnownCount.Should().Be(2);
    }
}
