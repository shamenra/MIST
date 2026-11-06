using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-006-03E:
/// The loadout synchronizer must never perform automatic
/// directory deletion during ordinary synchronization.
///
/// This is a source-level regression guard. Behavioral
/// integration tests are required separately.
/// </summary>
public sealed class MISTNoDirectoryDeletionContractTests
{
    [Fact]
    public void SynchronizerMustNotDeleteDirectories()
    {
        var directory = FindSynchronizerSource();

        var files = Directory
            .EnumerateFiles(
                directory,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
                !path.Split(
                    Path.DirectorySeparatorChar)
                .Any(component =>
                    component is "obj" or "bin"));

        var dangerousOperation = new Regex(
            @"\b(?:DeleteDirectory|Directory\s*\.\s*Delete)\s*\(",
            RegexOptions.CultureInvariant);

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);

            Assert.False(
                dangerousOperation.IsMatch(source),
                "Directory deletion is forbidden in the " +
                $"synchronizer source: {file}");
        }
    }

    [Fact]
    public void LegacyDirectoryCleanerMustNotReturn()
    {
        var file = Path.Combine(
            FindSynchronizerSource(),
            "ALoadoutSynchronizer.cs");

        var source = File.ReadAllText(file);

        Assert.False(
            Regex.IsMatch(
                source,
                @"\bCleanDirectories\s*\("),
            "The legacy directory cleaner must not " +
            "be reintroduced.");
    }

    private static string FindSynchronizerSource()
    {
        DirectoryInfo? current =
            new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate = Path.Combine(
                current.FullName,
                "src",
                "NexusMods.Abstractions.Loadouts.Synchronizers");

            if (Directory.Exists(candidate))
                return candidate;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "M.I.S.T. source tree could not be located.");
    }
}


/// <summary>
/// MIST-006-14A1 source-level safety contract. These checks guard the call
/// paths, but they do not replace behavioral integration tests.
/// </summary>
public sealed class MISTNonDestructiveUnmanageContractTests
{
    private static string Read(string relative)
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var file = Path.Combine(current.FullName, relative);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void UnmanageManagerMustNotDeactivateOrResetGame()
    {
        var source = Read("src/NexusMods.DataModel/LoadoutManager.cs");
        var beginning = source.IndexOf("public IJobTask<UnmanageGameJob", StringComparison.Ordinal);
        var ending = source.IndexOf("public async Task<LoadoutItemGroup.ReadOnly> InstallItemWrapper", beginning, StringComparison.Ordinal);
        Assert.True(beginning >= 0 && ending > beginning);
        var unmanage = source.Substring(beginning, ending - beginning);
        Assert.Contains("if (cleanGameFolder) throw", unmanage);
        Assert.Contains("deactivateIfActive: false", unmanage);
        Assert.DoesNotContain("DeactivateCurrentLoadout(", unmanage);
    }

    [Fact]
    public void UnmanageServiceMustRequireExplicitNonDestructiveBehavior()
    {
        var source = Read("src/NexusMods.DataModel/Synchronizer/SynchronizerService.cs");
        Assert.Contains("bool runGc = false, bool cleanGameFolder = false", source);
        Assert.Contains("if (cleanGameFolder) throw", source);
        Assert.Contains("cleanGameFolder: false", source);
    }

    [Fact]
    public void UninstallCommandMustFailClosedUntilArchivePurgeIsAudited()
    {
        var source = Read("src/NexusMods.App/Commandline/CleanupVerbs.cs");
        Assert.Contains("uninstall-app is blocked", source);
        Assert.DoesNotContain("DeleteDirectory(recursive: true)", source);
        Assert.DoesNotContain("syncService.UnManage", source);
    }

    [Fact]
    public void UiMustNotOfferCleanInstallOrResetOnUnmanage()
    {
        var source = Read("src/NexusMods.App.UI/Pages/MyGames/MyGamesViewModel.cs");
        Assert.Contains("runGc: false, cleanGameFolder: false", source);
        Assert.DoesNotContain("Clean folder", source);
        Assert.DoesNotContain("private async Task CleanGameFolder(", source);
    }
}
