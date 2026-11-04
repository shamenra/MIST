using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;
using NexusMods.Sdk.Hashes;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Loadouts;

/// <summary>
/// MIST-003B: Records a file that M.I.S.T. actually deployed.
///
/// This is deployment history, not a general filesystem snapshot.
/// Receipts must only be created after a successful managed write.
///
/// A receipt alone does not authorize modification. The current
/// file must still be verified against its deployed hash.
///
/// Original-file backups and user authorization are separate
/// requirements for operations that replace existing content.
/// </summary>
public partial class MISTManagedFileReceipt : IModelDefinition
{
    private const string Namespace = "MIST.Loadouts.ManagedFileReceipt";

    /// <summary>
    /// The installation in which M.I.S.T. deployed the file.
    /// </summary>
    public static readonly ReferenceAttribute<
        NexusMods.Sdk.Games.GameInstallMetadata> GameInstall =
        new(Namespace, nameof(GameInstall));

    /// <summary>
    /// The managed destination within the game installation.
    /// </summary>
    public static readonly GamePathParentAttribute Path =
        new(Namespace, nameof(Path));

    /// <summary>
    /// The hash of the content M.I.S.T. deployed.
    /// </summary>
    public static readonly HashAttribute DeployedHash =
        new(Namespace, nameof(DeployedHash));
}
