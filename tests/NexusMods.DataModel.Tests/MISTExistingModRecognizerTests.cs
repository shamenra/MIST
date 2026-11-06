using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NexusMods.DataModel.ModDiscovery;
using Xunit;

namespace NexusMods.DataModel.Tests;

public sealed class MISTExistingModRecognizerTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mist-mod-recognition-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "Game");
        public string Downloads => Path.Combine(Root, "Downloads");
        public Fixture()
        {
            Directory.CreateDirectory(Game);
            Directory.CreateDirectory(Downloads);
        }
        public void AddInstalled(string relative, string text)
        {
            var path = Path.Combine(Game, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        public string AddZip(string name, params (string Path, string Value)[] entries)
        {
            var path = Path.Combine(Downloads, name);
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var (entryPath, content) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entryPath).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
            return path;
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public async Task ExactZipEntryMatchesInstalledFileWithoutMutatingIt()
    {
        using var f = new Fixture();
        f.AddInstalled("r6/scripts/Example.reds", "script-version-one");
        var zip = f.AddZip("example.zip", ("archive-prefix/r6/scripts/Example.reds", "script-version-one"));
        var before = File.ReadAllBytes(Path.Combine(f.Game, "r6/scripts/Example.reds"));

        var report = await MISTExistingModRecognizer.ScanCyberpunkAsync(f.Game, f.Downloads);

        Assert.Equal("exact-archive-content", Assert.Single(report.Archives).State);
        Assert.Equal("unknown-nexus-origin", Assert.Single(report.Archives).IdentityState);
        Assert.Empty(report.UnknownInstalledFiles);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(f.Game, "r6/scripts/Example.reds")));
        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task SidecarNexusIdsRemainUnverifiedClaims()
    {
        using var f = new Fixture();
        f.AddInstalled("archive/pc/mod/Example.archive", "abc");
        var zip = f.AddZip("example.zip", ("archive/pc/mod/Example.archive", "abc"));
        File.WriteAllText(zip + ".meta", "{\"source\":\"nexus\",\"game\":\"cyberpunk2077\",\"modId\":42,\"fileId\":84}");

        var match = Assert.Single((await MISTExistingModRecognizer.ScanCyberpunkAsync(f.Game, f.Downloads)).Archives);

        Assert.Equal("sidecar-claimed-nexus-unverified", match.IdentityState);
        Assert.Equal(42L, match.NexusHint!.ModId);
        Assert.Equal(84L, match.NexusHint.FileId);
        Assert.Equal(1, match.ExactFiles);
    }

    [Fact]
    public async Task DifferentHashAtSamePathIsConflictNotVerifiedMod()
    {
        using var f = new Fixture();
        f.AddInstalled("r6/scripts/Example.reds", "customized");
        f.AddZip("example.zip", ("r6/scripts/Example.reds", "release-version"));

        var report = await MISTExistingModRecognizer.ScanCyberpunkAsync(f.Game, f.Downloads);
        var match = Assert.Single(report.Archives);

        Assert.Equal("path-conflict-only", match.State);
        Assert.Equal(0, match.ExactFiles);
        Assert.Equal(1, match.DifferentFiles);
        Assert.Single(report.UnknownInstalledFiles);
    }

    [Fact]
    public async Task MixedArchiveNeedsReviewAndUnknownFilesRemainUnknown()
    {
        using var f = new Fixture();
        f.AddInstalled("red4ext/plugins/Example.dll", "one");
        f.AddInstalled("r6/scripts/UserChange.reds", "custom");
        f.AddZip("example.zip", ("red4ext/plugins/Example.dll", "one"),
            ("r6/scripts/Missing.reds", "two"));

        var report = await MISTExistingModRecognizer.ScanCyberpunkAsync(f.Game, f.Downloads);

        Assert.Equal("partial-match", Assert.Single(report.Archives).State);
        Assert.Contains("r6/scripts/UserChange.reds", report.UnknownInstalledFiles);
    }

    [Theory]
    [InlineData("../../r6/scripts/evil.reds")]
    [InlineData("/r6/scripts/evil.reds")]
    [InlineData("C:/r6/scripts/evil.reds")]
    [InlineData("r6/scripts/../evil.reds")]
    public void RejectsUnsafeArchiveDestination(string archivePath)
    {
        Assert.Null(MISTExistingModRecognizer.TryGetCyberpunkDestination(archivePath));
    }

    [Fact]
    public void ReportLocationCannotBeNestedInScannedGame()
    {
        Assert.True(MISTExistingModRecognizer.IsWithin("/tmp/mist/Game/report.json", "/tmp/mist/Game"));
        Assert.False(MISTExistingModRecognizer.IsWithin("/tmp/mist/GameExtra/report.json", "/tmp/mist/Game"));
    }

    [Fact]
    public async Task RejectsOverlappingSearchRoots()
    {
        using var f = new Fixture();
        var nested = Path.Combine(f.Game, "downloads");
        Directory.CreateDirectory(nested);
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await MISTExistingModRecognizer.ScanCyberpunkAsync(f.Game, nested);
        });
    }
}
