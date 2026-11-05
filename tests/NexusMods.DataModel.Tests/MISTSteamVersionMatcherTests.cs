using System;
using System.Linq;
using NexusMods.Abstractions.Games.FileHashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTSteamVersionMatcherTests
{
    [Fact]
    public void OldBuildIsNotMistakenForNewBuildFromSharedDepot()
    {
        // Both versions share one depot, but differ in their content depot.
        ulong[] installed = [100, 201];
        ulong[][] versions = [[100, 201], [100, 202]];
        Assert.True(MISTSteamVersionMatcher.TrySelectUnique(
            installed, versions, out var index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void PartialMatchCannotClaimPublicVersion()
    {
        ulong[] installed = [100, 201];
        ulong[][] versions = [[100, 202]];
        Assert.False(MISTSteamVersionMatcher.TrySelectUnique(
            installed, versions, out var index));
        Assert.Equal(-1, index);
    }

    [Fact]
    public void UnknownDlcDepotPreventsPrematureCompatibilityClaim()
    {
        ulong[] installed = [100, 201, 999];
        ulong[][] versions = [[100, 201]];
        Assert.False(MISTSteamVersionMatcher.TrySelectUnique(installed, versions, out _));
    }

    [Fact]
    public void SharedManifestAmbiguityRejectsVersionGuess()
    {
        ulong[] installed = [100];
        ulong[][] versions = [[100, 201], [100, 202]];
        Assert.False(MISTSteamVersionMatcher.TrySelectUnique(installed, versions, out _));
    }

    [Fact]
    public void ExtraHistoricalManifestsInDefinitionAreAllowedIfUnique()
    {
        ulong[] installed = [100, 201];
        ulong[][] versions = [[100, 201, 301], [100, 202]];
        Assert.True(MISTSteamVersionMatcher.TrySelectUnique(installed, versions, out var index));
        Assert.Equal(0, index);
    }

    [Fact]
    public void EmptyAndInvalidInputsNeverGuess()
    {
        ulong[][] versions = [[100]];
        Assert.False(MISTSteamVersionMatcher.TrySelectUnique(
            Array.Empty<ulong>(), versions, out _));
        Assert.False(MISTSteamVersionMatcher.TrySelectUnique(
            new ulong[] {0}, versions, out _));
        Assert.False(MISTSteamVersionMatcher.TrySelectUnique(
            new ulong[] {100}, Array.Empty<ulong[]>(), out _));
    }

    [Fact]
    public void NoVersionCanBeSelectedIfMultipleRecordsDescribeSameManifests()
    {
        ulong[] installed = [100, 200];
        ulong[][] versions = [[100, 200], [100, 200]];
        Assert.False(MISTSteamVersionMatcher.TrySelectUnique(installed, versions, out _));
    }
}
