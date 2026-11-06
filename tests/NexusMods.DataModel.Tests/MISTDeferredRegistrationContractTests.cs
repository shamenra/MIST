using System;
using System.IO;
using Xunit;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-006-14B2B9.2 source-level regression checks for the experimental
/// registration split. These are NOT behavioral mutation-safety tests.
/// </summary>
public sealed class MISTDeferredRegistrationContractTests
{
    private static string Read(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void DeferredRegistrationMustBeOptInAndNotCallFirstSynchronization()
    {
        var source = Read("src/NexusMods.DataModel/LoadoutManager.cs");
        Assert.Contains("MIST_EXPERIMENTAL_DEFER_GAME_SYNC", source);
        Assert.Contains("if (needsFirstSynchronization && deferInitialSynchronization)", source);
        Assert.Contains("else if (needsFirstSynchronization)", source);
        Assert.Contains("allowFirstSynchronization: true", source);
    }

    [Fact]
    public void SynchronizerRejectsUnpreparedRegistrations()
    {
        var source = Read("src/NexusMods.DataModel/Synchronizer/SynchronizerService.cs");
        Assert.Contains("if (!allowFirstSynchronization &&", source);
        Assert.Contains("!GameInstallMetadata.LastSyncedLoadout.TryGetValue(loadout.Installation, out _)", source);
        Assert.Contains("if (last == default(LoadoutWithTxId))", source);
        Assert.Contains("return LoadoutSynchronizerState.Pending;", source);
    }

    [Fact]
    public void DirectActivationRejectsUnpreparedRegistrations()
    {
        var source = Read("src/NexusMods.DataModel/LoadoutManager.cs");
        var begin = source.IndexOf("public async ValueTask ActivateLoadout(", StringComparison.Ordinal);
        var end = source.IndexOf("public async ValueTask DeactivateCurrentLoadout(", begin, StringComparison.Ordinal);
        Assert.True(begin >= 0 && end > begin);
        var body = source.Substring(begin, end - begin);
        Assert.Contains("!GameInstallMetadata.LastSyncedLoadout.TryGetValue(loadout.Installation, out _)", body);
        Assert.Contains("throw new InvalidOperationException", body);
    }
}
