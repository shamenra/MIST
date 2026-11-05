using System;
using System.IO;
using System.Linq;
using NexusMods.Backend.Games.Locators;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTSteamLibraryCatalogTests
{
    [Fact]
    public void MISTOfflineManualLibrarySurvivesRestart()
    {
        using var fixture = new Fixture();

        var offline = Path.Combine(
            fixture.Root, "ExternalDrive", "SteamLibrary");

        var catalog = fixture.CreateCatalog();
        catalog.AddManual(offline);

        Assert.False(Directory.Exists(offline));

        var reloaded = fixture.CreateCatalog();

        var entry = Assert.Single(
            reloaded.Refresh().Where(x => x.Path == offline));

        Assert.True(entry.UserAdded);
        Assert.False(entry.IsOnline);
    }

    [Fact]
    public void MISTDiscoversLibraryFromSteamRegistry()
    {
        using var fixture = new Fixture();

        var steamRoot = Path.Combine(
            fixture.Home, ".local", "share", "Steam");

        var steamapps = Path.Combine(
            steamRoot, "steamapps");

        Directory.CreateDirectory(steamapps);

        var external = Path.Combine(
            fixture.Root, "External", "SteamLibrary");

        var externalApps = Path.Combine(
            external, "steamapps");

        Directory.CreateDirectory(externalApps);

        File.WriteAllText(
            Path.Combine(externalApps, "appmanifest_123.acf"),
            "test");

        File.WriteAllText(
            Path.Combine(steamapps, "libraryfolders.vdf"),
            "\"libraryfolders\" { \"1\" { \"path\" \"" +
            external + "\" } }");

        var catalog = fixture.CreateCatalog();

        var entry = Assert.Single(
            catalog.Refresh()
                .Where(x => x.Path == external));

        Assert.True(entry.IsOnline);
        Assert.False(entry.UserAdded);
        Assert.Equal(1, entry.ManifestCount);
    }

    [Fact]
    public void MISTDeduplicatesRegistryLibraryPaths()
    {
        using var fixture = new Fixture();

        var library = Path.Combine(
            fixture.Root, "Games", "SteamLibrary");

        Directory.CreateDirectory(
            Path.Combine(library, "steamapps"));

        var roots = new[]
        {
            Path.Combine(
                fixture.Home, ".local", "share", "Steam"),
            Path.Combine(
                fixture.Home, ".var", "app",
                "com.valvesoftware.Steam",
                "data", "Steam")
        };

        foreach (var root in roots)
        {
            var steamapps = Path.Combine(
                root, "steamapps");

            Directory.CreateDirectory(steamapps);

            File.WriteAllText(
                Path.Combine(
                    steamapps, "libraryfolders.vdf"),
                "\"path\" \"" + library + "\"");
        }

        var catalog = fixture.CreateCatalog();

        Assert.Single(
            catalog.Refresh()
                .Where(x => x.Path == library));
    }

    [Fact]
    public void MISTDisconnectedLibraryIsNotForgotten()
    {
        using var fixture = new Fixture();

        var library = Path.Combine(
            fixture.Root, "USB", "SteamLibrary");

        var steamapps = Path.Combine(
            library, "steamapps");

        Directory.CreateDirectory(steamapps);

        var catalog = fixture.CreateCatalog();

        catalog.AddManual(library);

        Assert.True(Assert.Single(
            catalog.Refresh()
                .Where(x => x.Path == library)).IsOnline);

        Directory.Delete(steamapps);

        var reloaded = fixture.CreateCatalog();

        var offline = Assert.Single(
            reloaded.Refresh()
                .Where(x => x.Path == library));

        Assert.False(offline.IsOnline);
        Assert.True(offline.UserAdded);

        Directory.CreateDirectory(steamapps);

        Assert.True(Assert.Single(
            reloaded.Refresh()
                .Where(x => x.Path == library)).IsOnline);
    }

    [Fact]
    public void MISTAssociatesExternalLibraryWithFlatpakSteam()
    {
        using var fixture = new Fixture();

        var steamRoot = Path.Combine(
            fixture.Home,
            ".var", "app", "com.valvesoftware.Steam",
            "data", "Steam");

        var steamapps = Path.Combine(
            steamRoot, "steamapps");

        Directory.CreateDirectory(steamapps);

        var external = Path.Combine(
            fixture.Root, "External", "SteamLibrary");

        Directory.CreateDirectory(
            Path.Combine(external, "steamapps"));

        File.WriteAllText(
            Path.Combine(steamapps, "libraryfolders.vdf"),
            "\"path\" \"" + external + "\"");

        var catalog = fixture.CreateCatalog();

        Assert.Equal(
            steamRoot,
            catalog.FindSteamRootForLibrary(external));
    }

    [Fact]
    public void MISTOfflineManualLibraryHasNoInventedSteamRoot()
    {
        using var fixture = new Fixture();

        var offline = Path.Combine(
            fixture.Root, "Disconnected", "SteamLibrary");

        var catalog = fixture.CreateCatalog();

        catalog.AddManual(offline);

        Assert.Null(
            catalog.FindSteamRootForLibrary(offline));

        var entry = Assert.Single(
            catalog.Refresh().Where(x => x.Path == offline));

        Assert.True(entry.UserAdded);
        Assert.False(entry.IsOnline);
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(
            Path.GetTempPath(),
            "MIST-LibraryCatalog-" +
            Guid.NewGuid().ToString("N"));

        public string Home => Path.Combine(Root, "home");

        private string StateFile => Path.Combine(
            Root, "state", "catalog.json");

        public Fixture()
        {
            Directory.CreateDirectory(Home);
        }

        public MISTSteamLibraryCatalog CreateCatalog()
        {
            return new MISTSteamLibraryCatalog(
                Home, StateFile);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
