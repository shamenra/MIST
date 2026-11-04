using DynamicData.Kernel;
using FluentAssertions;
using NexusMods.Abstractions.Loadouts.Synchronizers.Rules;
using NexusMods.Hashing.xxHash3;
using Xunit;
using static NexusMods.Abstractions.Loadouts.Synchronizers.Rules.Actions;

namespace NexusMods.DataModel.Tests;

/// <summary>
/// MIST-002: Characterization tests for inherited synchronization behavior.
/// These tests document existing behavior; they do not yet enforce
/// baseline completeness protection.
/// </summary>
public class MISTBaselineSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FileMissingFromDesiredStateIsScheduledForDeletion(
        bool alreadyArchived)
    {
        // A file exists on disk and in the previous disk state,
        // but has no entry in the desired game state.
        var hash = Hash.From(0xFAD01);

        var signature = SignatureBuilder.Build(
            diskHash: hash,
            prevHash: hash,
            loadoutHash: Optional.None<Hash>(),
            diskArchived: alreadyArchived,
            prevArchived: alreadyArchived,
            loadoutArchived: false,
            pathIsIgnored: false,
            sourceItemType: null
        );

        var actions = ActionMapping.MapActions(signature);

        // Document the inherited deletion behavior.
        actions.Should().HaveFlag(DeleteFromDisk);

        // An unarchived file should be backed up first.
        if (alreadyArchived)
        {
            actions.Should().NotHaveFlag(BackupFile);
        }
        else
        {
            actions.Should().HaveFlag(BackupFile);
        }
    }
}
