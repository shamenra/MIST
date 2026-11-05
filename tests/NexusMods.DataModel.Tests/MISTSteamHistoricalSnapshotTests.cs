using System;
using NexusMods.Abstractions.Games.FileHashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTSteamHistoricalSnapshotTests
{
    [Fact]
    public void HistoricalVersionWorksWhenNewestSnapshotHasNoMatch()
    {
        var snapshots = new (string Fingerprint, string? Version)[]
        {
            ("historical-file-baseline", "2.5"),
        };
        Assert.True(MISTSteamHistoricalSnapshotSelector.TrySelect(
            snapshots, out var selected, out var version));
        Assert.Equal(0, selected);
        Assert.Equal("2.5", version);
    }

    [Fact]
    public void PrefersHistoricalVersionBearingSnapshotWhenNewestLacksAlias()
    {
        var snapshots = new (string Fingerprint, string? Version)[]
        {
            ("same-files", null),
            ("same-files", "1.63"),
        };
        Assert.True(MISTSteamHistoricalSnapshotSelector.TrySelect(
            snapshots, out var selected, out var version));
        Assert.Equal(1, selected);
        Assert.Equal("1.63", version);
    }

    [Fact]
    public void PreservesOriginalFileBaselineEvenIfPublicVersionUnknown()
    {
        var snapshots = new (string Fingerprint, string? Version)[]
        {
            ("original-files", null),
        };
        Assert.True(MISTSteamHistoricalSnapshotSelector.TrySelect(
            snapshots, out var selected, out var version));
        Assert.Equal(0, selected);
        Assert.Null(version);
    }

    [Fact]
    public void ConflictingOriginalFileBaselinesFailClosed()
    {
        var snapshots = new (string Fingerprint, string? Version)[]
        {
            ("file-set-one", "2.5"),
            ("file-set-two", "2.5"),
        };
        Assert.False(MISTSteamHistoricalSnapshotSelector.TrySelect(
            snapshots, out _, out _));
    }

    [Fact]
    public void ConflictingVersionAliasesFailClosed()
    {
        var snapshots = new (string Fingerprint, string? Version)[]
        {
            ("same-files", "2.5"),
            ("same-files", "2.4"),
        };
        Assert.False(MISTSteamHistoricalSnapshotSelector.TrySelect(
            snapshots, out _, out _));
    }

    [Fact]
    public void DuplicateIdenticalAliasIsAcceptable()
    {
        var snapshots = new (string Fingerprint, string? Version)[]
        {
            ("same-files", "2.5"),
            ("same-files", "2.5"),
        };
        Assert.True(MISTSteamHistoricalSnapshotSelector.TrySelect(
            snapshots, out var selected, out var version));
        Assert.Equal(0, selected);
        Assert.Equal("2.5", version);
    }

    [Fact]
    public void MissingOrInvalidSnapshotsCannotResolve()
    {
        Assert.False(MISTSteamHistoricalSnapshotSelector.TrySelect(
            Array.Empty<(string, string?)>(), out _, out _));
        Assert.False(MISTSteamHistoricalSnapshotSelector.TrySelect(
            new (string, string?)[] { ("", "2.5") }, out _, out _));
    }
}
