using System;
using System.IO;
using NexusMods.Abstractions.Games.FileHashes;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTSteamBuildIdentityTests
{
    [Fact]
    public void DifferentLibrariesRetainDifferentVersionsOfSameGame()
    {
        using var temp = new TemporaryRoot();
        var older = WriteGame(temp.Root, "older", 19115871UL, 7537075278498631900UL);
        var newer = WriteGame(temp.Root, "newer", 26161058UL, 3524634124112875626UL);

        Assert.True(MISTSteamBuildIdentityReader.TryReadForGameFolder(older, out var first));
        Assert.True(MISTSteamBuildIdentityReader.TryReadForGameFolder(newer, out var second));
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(19115871UL, first.BuildId);
        Assert.Equal(26161058UL, second.BuildId);
        Assert.NotEqual(first.Signature, second.Signature);
        Assert.NotEqual(first.AppManifestPath, second.AppManifestPath);
    }

    [Fact]
    public void InvalidOrMissingManifestIsNotTreatedAsARecognizedVersion()
    {
        using var temp = new TemporaryRoot();
        var game = Path.Combine(temp.Root, "empty", "steamapps", "common", "Example Game");
        Directory.CreateDirectory(game);
        Assert.False(MISTSteamBuildIdentityReader.TryReadForGameFolder(game, out _));
        var steamapps = Directory.GetParent(game)!.Parent!.FullName;
        File.WriteAllText(Path.Combine(steamapps, "appmanifest_1091500.acf"),
            MakeManifest("Different Game", 3UL, 99UL));
        Assert.False(MISTSteamBuildIdentityReader.TryReadForGameFolder(game, out _));
    }

    [Fact]
    public void EveryInstalledDepotContributesToBuildSignature()
    {
        using var temp = new TemporaryRoot();
        var game = WriteGame(temp.Root, "test", 200UL, 300UL);
        var acf = Path.Combine(temp.Root, "test", "steamapps", "appmanifest_1091500.acf");
        Assert.True(MISTSteamBuildIdentityReader.TryReadForGameFolder(game, out var before));
        Assert.NotNull(before);
        File.WriteAllText(acf, MakeManifest("Example Game", 200UL, 300UL)
            .Replace("\"InstalledDepots\"\n{", "\"InstalledDepots\"\n{\n  \"1091502\" { \"manifest\" \"400\" }"));
        Assert.True(MISTSteamBuildIdentityReader.TryReadForGameFolder(game, out var after));
        Assert.NotNull(after);
        Assert.Equal(2, after.InstalledDepots.Count);
        Assert.NotEqual(before.Signature, after.Signature);
    }

    private static string WriteGame(string root, string library, ulong build, ulong manifest)
    {
        var baseDir = Path.Combine(root, library, "steamapps");
        var game = Path.Combine(baseDir, "common", "Example Game");
        Directory.CreateDirectory(game);
        File.WriteAllText(Path.Combine(baseDir, "appmanifest_1091500.acf"),
            MakeManifest("Example Game", build, manifest));
        return game;
    }

    private static string MakeManifest(string install, ulong build, ulong manifest) =>
        $"\"AppState\"\n{{\n\"appid\" \"1091500\"\n\"installdir\" \"{install}\"\n\"buildid\" \"{build}\"\n\"InstalledDepots\"\n{{\n\"1091501\" {{ \"manifest\" \"{manifest}\" }}\n}}\n}}";

    private sealed class TemporaryRoot : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(),
            "mist-buildid-" + Guid.NewGuid().ToString("N"));
        public TemporaryRoot() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
