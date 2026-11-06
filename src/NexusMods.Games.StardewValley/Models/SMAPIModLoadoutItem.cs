using NexusMods.Abstractions.Loadouts;
using NexusMods.MnemonicDB.Abstractions.Attributes;
using NexusMods.MnemonicDB.Abstractions.Models;

namespace NexusMods.Games.StardewValley.Models;

// Legacy persisted model. Preserve until a database migration removes it.
[Include<LoadoutItemGroup>]
public partial class SMAPIModLoadoutItem : IModelDefinition
{
    private const string Namespace = "NexusMods.StardewValley.SMAPIModLoadoutItem";

    public static readonly ReferenceAttribute<SMAPIManifestLoadoutFile> Manifest = new(Namespace, nameof(Manifest));
}
