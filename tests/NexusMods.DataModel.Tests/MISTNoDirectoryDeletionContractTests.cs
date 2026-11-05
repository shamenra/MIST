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
