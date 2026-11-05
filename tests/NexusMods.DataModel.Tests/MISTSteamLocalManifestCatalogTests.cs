using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using NexusMods.Abstractions.Games.FileHashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTSteamLocalManifestCatalogTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("a/../../outside.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows/system32/evil.dll")]
    [InlineData("dir//file.txt")]
    [InlineData("dir/./file.txt")]
    [InlineData("dir/trailing./file")]
    [InlineData("dir/file.txt/")]
    public void UnsafeManifestPathsAreRejected(string path)
    {
        Assert.False(MISTSteamLocalManifestRules.TryNormalizeRelativeFile(path, out _));
    }

    [Fact]
    public void BackslashesBecomeRelativeForwardSlashes()
    {
        Assert.True(MISTSteamLocalManifestRules.TryNormalizeRelativeFile(
            "archive\\pc\\content\\file.bin", out var path));
        Assert.Equal("archive/pc/content/file.bin", path);
    }

    [Fact]
    public void SameBuildInDifferentGameFoldersHasIndependentCatalogFiles()
    {
        var identity = new MISTSteamBuildIdentity(1091500, 26161058,
            "/some/steamapps/appmanifest_1091500.acf",
            new System.Collections.Generic.Dictionary<uint, ulong> { [1091501] = 3524634124112875626 });
        var older = MISTSteamLocalManifestRules.CatalogFilePath(
            Path.GetTempPath(), identity, Path.Combine(Path.GetTempPath(), "older"));
        var newer = MISTSteamLocalManifestRules.CatalogFilePath(
            Path.GetTempPath(), identity, Path.Combine(Path.GetTempPath(), "newer"));
        Assert.NotEqual(older, newer);
        Assert.Contains(identity.Signature, older);
    }

    [Fact]
    public void MissingDepotsPreventCompleteBaseline()
    {
        var identity = new MISTSteamBuildIdentity(1091500, 200,
            "unused", new System.Collections.Generic.Dictionary<uint, ulong>
            {
                [1] = 10, [2] = 20,
            });
        var path = Path.Combine(Path.GetTempPath(), "one-game");
        var capture = new MISTSteamLocalManifestCatalog(1091500, 200, identity.Signature,
            MISTSteamLocalManifestRules.InstallationPathHash(path), DateTimeOffset.UtcNow,
            [new MISTSteamDepotCatalog(1, 10,
                [new MISTSteamManifestFile("file.bin", 1, new string('A', 40))])], [2]);
        Assert.True(MISTSteamLocalManifestRules.ValidCatalog(capture, identity, path));
        Assert.False(capture.Complete);
    }

    [Fact]
    public async Task FileMustMatchCachedManifestSha1AndSize()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-sha1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Encoding.UTF8.GetBytes("original game data");
            await File.WriteAllBytesAsync(Path.Combine(root, "original.bin"), bytes);
            var record = new MISTSteamManifestFile("original.bin", bytes.Length,
                Convert.ToHexString(SHA1.HashData(bytes)));
            Assert.True(await MISTSteamLocalManifestRules.MatchesInstalledFileAsync(root, record));
            await File.WriteAllTextAsync(Path.Combine(root, "original.bin"), "modified game data");
            Assert.False(await MISTSteamLocalManifestRules.MatchesInstalledFileAsync(root, record));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FileVerificationCannotEscapeTheGameFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "mist-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var record = new MISTSteamManifestFile("../outside.txt", 1, new string('A', 40));
            Assert.False(await MISTSteamLocalManifestRules.MatchesInstalledFileAsync(root, record));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
