using NexusMods.Abstractions.Loadouts.Synchronizers.Rules;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// MIST-003B: Validates a planned set of disk mutations before execution.
///
/// The ownership callback must be backed by independent M.I.S.T.
/// deployment evidence. A disk snapshot or previous-state hash
/// alone is not sufficient evidence of ownership.
///
/// This policy does not replace filesystem-level checks at execution
/// time, including protection against files changing after preflight.
/// </summary>
public static class MISTMutationPreflight
{
    public static void EnsureAuthorized(
        IReadOnlyDictionary<GamePath, SyncNode> plan,
        Func<GamePath, bool> existsOnDisk,
        Func<GamePath, bool> hasVerifiedManagedReceipt)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(existsOnDisk);
        ArgumentNullException.ThrowIfNull(hasVerifiedManagedReceipt);

        foreach (var (path, node) in plan)
        {
            var deleting = node.Actions.HasFlag(Actions.DeleteFromDisk);

            var writing = node.Actions.HasFlag(Actions.ExtractToDisk) ||
                          node.Actions.HasFlag(Actions.WriteIntrinsic);

            if (!deleting && !writing)
                continue;

            var exists = existsOnDisk(path);

            if (deleting && !exists)
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. mutation plan is stale: " +
                    $"file '{path.Path}' is missing.");
            }

            // A new file does not overwrite existing content.
            if (!exists)
                continue;

            // Existing files require independently verified ownership.
            if (!hasVerifiedManagedReceipt(path))
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. cannot modify '{path.Path}': " +
                    "verified managed-file ownership is unavailable.");
            }
        }
    }
}
