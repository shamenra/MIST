using NexusMods.Abstractions.Games.FileHashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public sealed class MISTSteamPublicVersionCatalogTests
{
    [Fact]
    public void KnownCyberpunkManifestResolvesToPublicVersion()
    {
        Assert.True(MISTSteamPublicVersionCatalog.TryResolve(
            ["3524634124112875626"], out var version));
        Assert.Equal("2.5", version);
    }

    [Fact]
    public void KnownOblivionManifestResolvesToPublicVersion()
    {
        Assert.True(MISTSteamPublicVersionCatalog.TryResolve(
            ["7537075278498631900"], out var version));
        Assert.Equal("1.2", version);
    }

    [Fact]
    public void UnknownHistoricalBuildRemainsUnknown() =>
        Assert.False(MISTSteamPublicVersionCatalog.TryResolve(
            ["1111111111111111111"], out _));

    [Fact]
    public void ExtraDepotCannotInheritVersion() =>
        Assert.False(MISTSteamPublicVersionCatalog.TryResolve(
            ["3524634124112875626", "2222222222222222222"], out _));

    [Fact]
    public void DuplicateManifestCannotInheritVersion() =>
        Assert.False(MISTSteamPublicVersionCatalog.TryResolve(
            ["3524634124112875626", "3524634124112875626"], out _));

    [Fact]
    public void MixedGamesCannotInheritVersion() =>
        Assert.False(MISTSteamPublicVersionCatalog.TryResolve(
            ["3524634124112875626", "7537075278498631900"], out _));

    [Fact]
    public void MalformedManifestCannotInheritVersion() =>
        Assert.False(MISTSteamPublicVersionCatalog.TryResolve(
            ["not-a-manifest"], out _));
}
