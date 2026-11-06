using System;
using System.IO;
using System.Linq;
using NexusMods.DataModel.ModDiscovery;
using Xunit;

namespace NexusMods.DataModel.Tests;

public sealed class MISTModDiscoveryPipelineTests
{
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "mist-b2b-" + Guid.NewGuid().ToString("N"));
        public string Game => Path.Combine(Root, "Cyberpunk 2077");
        public string Other => Path.Combine(Root, "OtherGame");

        public Fixture()
        {
            Directory.CreateDirectory(Game);
            Directory.CreateDirectory(Other);
        }

        public string Add(string absoluteRoot, string relative, string text)
        {
            var target = Path.Combine(absoluteRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, text);
            return target;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    [Fact]
    public void FindsEmbeddedVersionButNotUnrelatedGameMetadata()
    {
        using var f = new Fixture();
        f.Add(f.Game, "r6/scripts/virtual-atelier-full/Version.reds", "const let version = \"2.18.3\";");
        f.Add(f.Other, "r6/scripts/SomeOtherMod/Version.reds", "version = \"999.0.0\"");
        var report = MISTModDiscoveryPipeline.Scan(f.Game);
        Assert.Equal("mist-game-scoped-discovery/v2b", report.Schema);
        Assert.Single(report.Inventory.CandidateGroups);
        Assert.Contains(report.IdentityHints, h => h.GroupKey == "virtualatelierfull" &&
            h.Kind == "embedded-version" && h.Value == "2.18.3");
        Assert.DoesNotContain(report.IdentityHints, h => h.Value == "999.0.0");
        Assert.All(report.Modules, m => Assert.Equal("completed", m.Status));
    }

    [Fact]
    public void NexusReferenceIsNotAssertedAsVerifiedProvenance()
    {
        using var f = new Fixture();
        f.Add(f.Game, "red4ext/plugins/ArchiveXL/readme.md",
            "Requires https://www.nexusmods.com/cyberpunk2077/mods/12345");
        var report = MISTModDiscoveryPipeline.Scan(f.Game);
        var hint = Assert.Single(report.IdentityHints);
        Assert.Equal("nexus-reference", hint.Kind);
        Assert.Equal("12345", hint.Value);
        Assert.Equal("unverified-could-be-dependency", hint.Reliability);
    }

    [Fact]
    public void DoesNotMergePotentiallyRelatedMods()
    {
        using var f = new Fixture();
        f.Add(f.Game, "archive/pc/mod/SilentSilencers.archive", "contents-a");
        f.Add(f.Game, "r6/scripts/SilentSilencersAndKnives.reds", "contents-b");
        var report = MISTModDiscoveryPipeline.Scan(f.Game);
        Assert.Equal(2, report.Inventory.CandidateGroups.Count);
        Assert.Single(report.PossibleRelationships);
        Assert.Equal("possible-component-or-related-mod", report.PossibleRelationships[0].Relation);
    }

    [Fact]
    public void BoundedFingerprintIsOfActualGameFileNotUnrelatedFile()
    {
        using var f = new Fixture();
        f.Add(f.Game, "red4ext/plugins/Codeware/Codeware.dll", "codeware-bytes");
        f.Add(f.Other, "red4ext/plugins/Codeware/Codeware.dll", "other-bytes");
        var report = MISTModDiscoveryPipeline.Scan(f.Game);
        var fingerprint = Assert.Single(report.ContentFingerprints);
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes("codeware-bytes")));
        Assert.Equal(expected, fingerprint.Sha256);
        Assert.Equal("codeware", fingerprint.GroupKey);
    }

    [Fact]
    public void GeneratedLogsAndSettingsCannotBeTreatedAsSourceVersions()
    {
        using var f = new Fixture();
        f.Add(f.Game, "bin/x64/plugins/cyber_engine_tweaks/mods/MuchBetterAI/settings.json",
            "{\"version\":\"999.0.0\"}");
        f.Add(f.Game, "bin/x64/plugins/cyber_engine_tweaks/mods/MuchBetterAI/MuchBetterAI.log",
            "version = 555.0.0");
        var report = MISTModDiscoveryPipeline.Scan(f.Game);
        Assert.Empty(report.IdentityHints);
        Assert.Empty(report.ContentFingerprints);
    }

    [Fact]
    public void SymlinkedModDirectoryIsNeverReadOutsideSelectedGame()
    {
        using var f = new Fixture();
        var outside = f.Add(f.Other, "Sensitive/Version.reds", "version = \"666.0.0\"");
        var root = Path.Combine(f.Game, "r6", "scripts");
        Directory.CreateDirectory(root);
        var link = Path.Combine(root, "Outside");
        try { Directory.CreateSymbolicLink(link, Path.GetDirectoryName(outside)!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        { return; }
        var report = MISTModDiscoveryPipeline.Scan(f.Game);
        Assert.Empty(report.IdentityHints);
        Assert.Empty(report.Inventory.CandidateGroups);
        Assert.Contains(report.Inventory.SkippedPaths, x => x.Contains("symlink", StringComparison.Ordinal));
    }
}
