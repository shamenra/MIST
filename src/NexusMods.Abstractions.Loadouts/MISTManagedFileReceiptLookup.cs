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
    /// <summary>
    /// Returns a receipt ID only when exactly one receipt exists
    /// for this installation and destination AND its hash matches
    /// the actual observed file content.
    ///
    /// The caller must obtain observedHash from the live file.
    /// A receipt is not independent filesystem authorization.
    /// </summary>
    public static EntityId? FindExactlyOneMatchingReceiptId(
        IDb database,
        EntityId installationId,
        GamePath path,
        Hash observedHash)
    {
        ArgumentNullException.ThrowIfNull(database);

        var expectedPath =
            path.ToGamePathParentTuple(installationId);

        // Use the generated installation back-reference rather
        // than scanning all receipts across every game.
        var matches = MISTManagedFileReceipt
            .FindByGameInstall(database, installationId)
            .Where(receipt =>
                receipt.GameInstallId.Value == installationId &&
                receipt.Path.Equals(expectedPath))
            .Take(2)
            .ToArray();

        // Duplicate records are ambiguous even when their
        // hashes are identical.
        if (matches.Length != 1)
            return null;

        if (matches[0].DeployedHash != observedHash)
            return null;

        return matches[0].Id;
    }

    public static bool HasExactlyOneMatchingReceipt(
        IDb database,
        EntityId installationId,
        GamePath path,
        Hash observedHash)
    {
        return FindExactlyOneMatchingReceiptId(
            database,
            installationId,
            path,
            observedHash).HasValue;
    }
}
