using NexusMods.Hashing.xxHash3;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Loadouts;

/// <summary>
/// MIST-003B.4: Resolves managed-file deployment evidence.
///
/// The observed hash must come from the current file contents.
/// This class does not read or modify the filesystem.
///
/// A receipt is historical evidence, not permanent authorization
/// to change a file.
/// </summary>
public static class MISTManagedFileReceiptLookup
{
    public static bool HasExactlyOneMatchingReceipt(
        IDb database,
        EntityId installationId,
        GamePath path,
        Hash observedHash)
    {
        ArgumentNullException.ThrowIfNull(database);

        var expectedPath =
            path.ToGamePathParentTuple(installationId);

        // A missing receipt or ambiguous receipt history
        // must not authorize an operation.
        var matches = MISTManagedFileReceipt
            .All(database)
            .Where(receipt =>
                receipt.GameInstallId.Value == installationId &&
                receipt.Path.Equals(expectedPath))
            .Take(2)
            .ToArray();

        return matches.Length == 1 &&
               matches[0].DeployedHash == observedHash;
    }
}
