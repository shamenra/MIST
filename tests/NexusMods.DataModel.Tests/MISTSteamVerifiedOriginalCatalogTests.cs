using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NexusMods.Abstractions.Games.FileHashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public sealed class MISTSteamVerifiedOriginalCatalogTests
{
    private const uint AppId = 1091500;
    private const uint DepotId = 1091501;
    private const ulong ManifestId = 3524634124112875626;

    private static (MISTSteamBuildIdentity Identity,
        MISTSteamLocalManifestCatalog Catalog) Create(
            string root, params MISTSteamManifestFile[] files)
    {
        var identity = new MISTSteamBuildIdentity(AppId, 101,
            "unused.acf", new Dictionary<uint, ulong> { [DepotId] = ManifestId });
        var snapshot = new MISTSteamLocalManifestCatalog(AppId, 101,
            identity.Signature, MISTSteamLocalManifestRules.InstallationPathHash(root),
            DateTimeOffset.UtcNow,
            [new MISTSteamDepotCatalog(DepotId, ManifestId, files)], []);
        return (identity, snapshot);
    }

    [Fact]
    public async Task CorrectBytesCanBeRecognizedAsOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-verified-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Encoding.UTF8.GetBytes("original steam content");
            await File.WriteAllBytesAsync(Path.Combine(root, "file.dat"), bytes);
            var record = new MISTSteamManifestFile("file.dat", bytes.Length,
                Convert.ToHexString(SHA1.HashData(bytes)));
            var (identity, catalog) = Create(root, record);
            Assert.Equal(record, await MISTSteamVerifiedOriginalCatalog.VerifyInstalledFileAsync(
                root, catalog, identity, "file.dat"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task ChangedBytesAreNotPromotedToOriginal()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-changed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Encoding.UTF8.GetBytes("original steam content");
            await File.WriteAllBytesAsync(Path.Combine(root, "file.dat"), bytes);
            var record = new MISTSteamManifestFile("file.dat", bytes.Length,
                Convert.ToHexString(SHA1.HashData(bytes)));
            var (identity, catalog) = Create(root, record);
            await File.WriteAllTextAsync(Path.Combine(root, "file.dat"), "replaced steam content");
            Assert.Null(await MISTSteamVerifiedOriginalCatalog.VerifyInstalledFileAsync(
                root, catalog, identity, "file.dat"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void WrongBuildOrInstallationCannotReuseSnapshot()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-build-" + Guid.NewGuid().ToString("N"));
        var file = new MISTSteamManifestFile("file.dat", 10, new string('A', 40));
        var (identity, catalog) = Create(root, file);
        var different = identity with { BuildId = 102 };
        Assert.False(MISTSteamVerifiedOriginalCatalog.TryFindExpectedFile(
            catalog, different, root, "file.dat", out _));
        Assert.False(MISTSteamVerifiedOriginalCatalog.TryFindExpectedFile(
            catalog, identity, root + "-other", "file.dat", out _));
    }

    [Fact]
    public void MissingDepotPreventsOriginalFilePromotion()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-incomplete");
        var (identity, catalog) = Create(root,
            new MISTSteamManifestFile("a.txt", 1, new string('A', 40)));
        var incomplete = catalog with { MissingDepots = [999] };
        Assert.False(MISTSteamVerifiedOriginalCatalog.TryFindExpectedFile(
            incomplete, identity, root, "a.txt", out _));
    }

    [Fact]
    public void ConflictingCrossDepotFileHashesAreRejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-overlap");
        var (oldIdentity, catalog) = Create(root,
            new MISTSteamManifestFile("same.txt", 5, new string('A', 40)));
        var identity = oldIdentity with
        {
            InstalledDepots = new Dictionary<uint, ulong>
            {
                [DepotId] = ManifestId,
                [222] = 333,
            },
        };
        catalog = catalog with
        {
            BuildSignature = identity.Signature,
            Depots =
            [
                catalog.Depots[0],
                new MISTSteamDepotCatalog(222, 333,
                    [new MISTSteamManifestFile("same.txt", 5, new string('B', 40))]),
            ],
        };
        Assert.False(MISTSteamVerifiedOriginalCatalog.TryFindExpectedFile(
            catalog, identity, root, "same.txt", out _));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("dir//file")]
    public void UnsafePathsCannotBeLookedUp(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-unsafe");
        var (identity, catalog) = Create(root,
            new MISTSteamManifestFile("valid.txt", 1, new string('A', 40)));
        Assert.False(MISTSteamVerifiedOriginalCatalog.TryFindExpectedFile(
            catalog, identity, root, path, out _));
    }
}
