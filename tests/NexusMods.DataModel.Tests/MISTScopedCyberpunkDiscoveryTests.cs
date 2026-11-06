using System;
using System.IO;
using System.Linq;
using NexusMods.DataModel.ModDiscovery;
using Xunit;

namespace NexusMods.DataModel.Tests;

public sealed class MISTScopedCyberpunkDiscoveryTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mist-b2a-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "Cyberpunk 2077");
        public string OtherGame => Path.Combine(Root, "Wurm Unlimited");
        public string Downloads => Path.Combine(Root, "Downloads");

        public Fixture()
        {
            Directory.CreateDirectory(Game);
            Directory.CreateDirectory(OtherGame);
            Directory.CreateDirectory(Downloads);
        }

        public string Add(string absoluteRoot, string relative, string content = "mod-data")
        {
            var full = Path.Combine(absoluteRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void DoesNotInspectWurmOrDownloadsEvenIfTheyContainModShapedFiles()
    {
        using var f = new Fixture();
        f.Add(f.Game, "r6/scripts/StealthRunner/init.reds");
        f.Add(f.OtherGame, "r6/scripts/WurmUnlimited/init.reds");
        f.Add(f.Downloads, "r6/scripts/WurmUnlimited/more.reds");

        var report = MISTScopedCyberpunkDiscovery.Scan(f.Game);
        Assert.Equal(1, report.InstalledFileCount);
        Assert.Single(report.CandidateGroups);
        Assert.Equal("stealthrunner", report.CandidateGroups[0].Key);
        Assert.False(System.Text.Json.JsonSerializer.Serialize(report)
            .Contains("Wurm", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GroupsStealthRunnerFromSeveralGameSubdirectories()
    {
        using var f = new Fixture();
        f.Add(f.Game, "r6/scripts/StealthRunner/init.reds");
        f.Add(f.Game, "r6/tweaks/StealthRunner/a.yaml");
        f.Add(f.Game, "r6/config/redsUserHints/StealthRunner.toml");
        f.Add(f.Game, "archive/pc/mod/StealthRunner.archive");
        f.Add(f.Game, "archive/pc/mod/StealthRunner.archive.xl");
        var group = Assert.Single(MISTScopedCyberpunkDiscovery.Scan(f.Game).CandidateGroups);
        Assert.Equal("stealthrunner", group.Key);
        Assert.Equal(5, group.Files.Count);
        Assert.Equal("structural-candidate", group.Confidence);
        Assert.Contains(group.Evidence, e => e.Contains("REDscript folder", StringComparison.Ordinal));
    }

    [Fact]
    public void CetSubModsStaySeparateFromTheFramework()
    {
        using var f = new Fixture();
        f.Add(f.Game, "bin/x64/plugins/cyber_engine_tweaks.asi");
        f.Add(f.Game, "bin/x64/plugins/cyber_engine_tweaks/config.json");
        f.Add(f.Game, "bin/x64/plugins/cyber_engine_tweaks/mods/MuchBetterAI/init.lua");
        f.Add(f.Game, "bin/x64/plugins/cyber_engine_tweaks/mods/StealthRunner/init.lua");
        var report = MISTScopedCyberpunkDiscovery.Scan(f.Game);
        Assert.Equal(3, report.CandidateGroups.Count);
        Assert.Equal(2, report.CandidateGroups.Single(g => g.Key == "cyberenginetweaks").Files.Count);
        Assert.Single(report.CandidateGroups.Single(g => g.Key == "muchbetterai").Files);
        Assert.Single(report.CandidateGroups.Single(g => g.Key == "stealthrunner").Files);
    }

    [Fact]
    public void GeneratedAndMutableFilesAreHintsNotOwnership()
    {
        using var f = new Fixture();
        f.Add(f.Game, "red4ext/plugins/ArchiveXL/ArchiveXL.dll");
        f.Add(f.Game, "red4ext/plugins/ArchiveXL/ArchiveXL.log");
        f.Add(f.Game, "red4ext/plugins/ArchiveXL/ArchiveXL.yaml");
        var group = Assert.Single(MISTScopedCyberpunkDiscovery.Scan(f.Game).CandidateGroups);
        Assert.Equal("package-candidate", group.Files.Single(x => x.Path.EndsWith(".dll")).Role);
        Assert.Equal("possibly-generated", group.Files.Single(x => x.Path.EndsWith(".log")).Role);
        Assert.Equal("potentially-mutable", group.Files.Single(x => x.Path.EndsWith(".yaml")).Role);
        Assert.All(group.Evidence, e => Assert.False(e.Contains("verified Nexus", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void AvoidsVanillaGameConfigAndUnrelatedGameSubtrees()
    {
        using var f = new Fixture();
        f.Add(f.Game, "r6/config/settings/options.json");
        f.Add(f.Game, "engine/config/platform/pc/rendering.ini");
        f.Add(f.Game, "bin/x64/Cyberpunk2077.exe");
        f.Add(f.Game, "r6/config/redsUserHints/ArchiveXL.toml");
        var report = MISTScopedCyberpunkDiscovery.Scan(f.Game);
        Assert.Equal(1, report.InstalledFileCount);
        Assert.Equal("archivexl", Assert.Single(report.CandidateGroups).Key);
    }

    [Fact]
    public void SymlinkToExternalModFolderMustNotEscapeGame()
    {
        using var f = new Fixture();
        f.Add(f.OtherGame, "secret/WurmMod.lua");
        var target = Path.Combine(f.OtherGame, "secret");
        var parent = Path.Combine(f.Game, "r6", "scripts");
        Directory.CreateDirectory(parent);
        var link = Path.Combine(parent, "ExternalWurm");
        try { Directory.CreateSymbolicLink(link, target); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { return; /* Windows test runners may forbid links; Ubuntu CI exercises this path. */ }
        var report = MISTScopedCyberpunkDiscovery.Scan(f.Game);
        Assert.Empty(report.CandidateGroups);
        Assert.Equal(0, report.InstalledFileCount);
        Assert.Contains(report.SkippedPaths, x => x.Contains("symlink", StringComparison.Ordinal));
        Directory.Delete(link);
    }

    [Fact]
    public void RejectsLinkedIntermediateKnownRoot()
    {
        using var f = new Fixture();
        var outside = Path.Combine(f.OtherGame, "external");
        f.Add(f.OtherGame, "external/Sneaky.reds");
        var link = Path.Combine(f.Game, "r6");
        try { Directory.CreateSymbolicLink(link, outside); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { return; }
        var report = MISTScopedCyberpunkDiscovery.Scan(f.Game);
        Assert.Equal(0, report.InstalledFileCount);
        Assert.Contains(report.SkippedPaths, x => x.Contains("r6/scripts", StringComparison.Ordinal));
        Directory.Delete(link);
    }

    [Fact]
    public void ReportCannotBeWrittenToGameOrThroughLinkedParent()
    {
        using var f = new Fixture();
        Assert.False(MISTScopedCyberpunkDiscovery.IsSafeReportPath(Path.Combine(f.Game, "report.json"), f.Game));
        Assert.True(MISTScopedCyberpunkDiscovery.IsSafeReportPath(Path.Combine(f.Root, "report.json"), f.Game));
        var link = Path.Combine(f.Root, "alias");
        try { Directory.CreateSymbolicLink(link, f.Game); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or PlatformNotSupportedException or IOException)
        { return; }
        Assert.False(MISTScopedCyberpunkDiscovery.IsSafeReportPath(Path.Combine(link, "report.json"), f.Game));
        Directory.Delete(link);
    }

    [Fact]
    public void FlatGenericNamesRemainUnattributed()
    {
        using var f = new Fixture();
        f.Add(f.Game, "r6/scripts/config.reds");
        var report = MISTScopedCyberpunkDiscovery.Scan(f.Game);
        Assert.Empty(report.CandidateGroups);
        Assert.Single(report.UnattributedFiles);
    }
}
