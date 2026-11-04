using System.Collections.Immutable;
using NexusMods.Abstractions.Steam;
using NexusMods.Abstractions.Steam.DTOs;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Paths;
using NexusMods.Sdk.Games;
using Xunit;

namespace NexusMods.Networking.Steam.Tests;

public class MISTSteamManifestResolverTests
{
    private static readonly SteamInstalledDepot InstalledDepot = new(420, 101);

    [Fact]
    public async Task ResolvesMatchingManifest()
    {
        var session = new FakeSteamSession(
            MakeProduct(),
            MakeManifest());

        var result = await SteamInstalledManifestResolver.ResolveAsync(
            session, MakeInstallation(), InstalledDepot);

        Assert.Same(session.Manifest, result);
        Assert.Equal(1, session.ManifestRequests);
        Assert.Equal("public", session.LastBranch);
        Assert.Equal((uint)42, session.LastAppId?.Value);
        Assert.Equal((uint)420, session.LastDepotId?.Value);
        Assert.Equal((ulong)101, session.LastManifestId?.Value);
    }

    [Fact]
    public async Task RejectsNonSteamInstallation()
    {
        var session = NewSession();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SteamInstalledManifestResolver.ResolveAsync(
                session,
                MakeInstallation(store: GameStore.EGS),
                InstalledDepot));

        Assert.Equal(0, session.ProductRequests);
    }

    [Fact]
    public async Task RejectsInvalidAppId()
    {
        var session = NewSession();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SteamInstalledManifestResolver.ResolveAsync(
                session,
                MakeInstallation(storeIdentifier: "invalid"),
                InstalledDepot));

        Assert.Equal(0, session.ProductRequests);
    }

    [Fact]
    public async Task RejectsUnassociatedDepot()
    {
        var session = NewSession();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SteamInstalledManifestResolver.ResolveAsync(
                session,
                MakeInstallation(),
                new SteamInstalledDepot(999, 101)));

        Assert.Equal(0, session.ProductRequests);
    }

    [Fact]
    public async Task RejectsWrongProduct()
    {
        var session = new FakeSteamSession(
            MakeProduct(appId: 43),
            MakeManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SteamInstalledManifestResolver.ResolveAsync(
                session, MakeInstallation(), InstalledDepot));

        Assert.Equal(0, session.ManifestRequests);
    }

    [Fact]
    public async Task RejectsMissingManifestBranch()
    {
        var session = new FakeSteamSession(
            MakeProduct(manifestId: 999),
            MakeManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SteamInstalledManifestResolver.ResolveAsync(
                session, MakeInstallation(), InstalledDepot));

        Assert.Equal(0, session.ManifestRequests);
    }

    [Fact]
    public async Task RejectsAmbiguousBranches()
    {
        var session = new FakeSteamSession(
            MakeProduct(branches: ["public", "beta"]),
            MakeManifest());

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SteamInstalledManifestResolver.ResolveAsync(
                session, MakeInstallation(), InstalledDepot));

        Assert.Equal(0, session.ManifestRequests);
    }

    [Theory]
    [InlineData(999U, 101UL)]
    [InlineData(420U, 999UL)]
    public async Task RejectsMismatchedDownloadedManifest(
        uint depotId,
        ulong manifestId)
    {
        var session = new FakeSteamSession(
            MakeProduct(),
            MakeManifest(depotId, manifestId));

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await SteamInstalledManifestResolver.ResolveAsync(
                session, MakeInstallation(), InstalledDepot));

        Assert.Equal(1, session.ManifestRequests);
    }

    private static GameLocatorResult MakeInstallation(
        GameStore? store = null,
        string storeIdentifier = "42")
    {
        return new GameLocatorResult
        {
            Game = null!,
            Path = default,
            Store = store ?? GameStore.Steam,
            StoreIdentifier = storeIdentifier,
            LocatorIds = ImmutableArray.Create(LocatorId.From("101")),
            SteamInstalledDepots = ImmutableArray.Create(InstalledDepot),
            Locator = null!,
        };
    }

    private static ProductInfo MakeProduct(
        uint appId = 42,
        uint depotId = 420,
        ulong manifestId = 101,
        string[]? branches = null)
    {
        var manifests = (branches ?? ["public"]).ToDictionary(
            branch => branch,
            _ => new ManifestInfo
            {
                ManifestId = ManifestId.From(manifestId),
                Size = Size.FromLong(0),
                DownloadSize = Size.FromLong(0),
            });

        return new ProductInfo
        {
            ChangeNumber = 1,
            AppId = AppId.From(appId),
            Depots =
            [
                new Depot
                {
                    DepotId = DepotId.From(depotId),
                    OsList = ["windows"],
                    Manifests = manifests,
                },
            ],
        };
    }

    private static Manifest MakeManifest(
        uint depotId = 420,
        ulong manifestId = 101)
    {
        return new Manifest
        {
            DepotId = DepotId.From(depotId),
            ManifestId = ManifestId.From(manifestId),
            Files = [],
            CreationTime = DateTimeOffset.UnixEpoch,
            TotalCompressedSize = Size.FromLong(0),
            TotalUncompressedSize = Size.FromLong(0),
        };
    }

    private static FakeSteamSession NewSession()
        => new(MakeProduct(), MakeManifest());

    private sealed class FakeSteamSession(
        ProductInfo product,
        Manifest manifest) : ISteamSession
    {
        public Manifest Manifest => manifest;

        public int ProductRequests { get; private set; }
        public int ManifestRequests { get; private set; }

        public string? LastBranch { get; private set; }
        public AppId? LastAppId { get; private set; }
        public DepotId? LastDepotId { get; private set; }
        public ManifestId? LastManifestId { get; private set; }

        public Task Connect(CancellationToken token)
            => Task.CompletedTask;

        public Task<ProductInfo> GetProductInfoAsync(
            AppId appId,
            CancellationToken cancellationToken = default)
        {
            ProductRequests++;
            return Task.FromResult(product);
        }

        public Task<Manifest> GetManifestContents(
            AppId appId,
            DepotId depotId,
            ManifestId manifestId,
            string branch,
            CancellationToken token = default)
        {
            ManifestRequests++;
            LastAppId = appId;
            LastDepotId = depotId;
            LastManifestId = manifestId;
            LastBranch = branch;

            return Task.FromResult(manifest);
        }

        public Stream GetFileStream(
            AppId appId,
            Manifest manifest,
            RelativePath file)
        {
            throw new NotSupportedException(
                "Offline manifest tests never download files.");
        }
    }
}
