using NexusMods.Abstractions.Loadouts.Synchronizers;
using NexusMods.Sdk.Games;

namespace NexusMods.Games.OblivionRemastered;

/// <summary>
/// Initial synchronization scope is limited to ~mods.
///
/// Other Oblivion Remastered locations must not be treated
/// as managed mod targets until their integration is built.
/// </summary>
public class OblivionRemasteredSynchronizer(
    IServiceProvider provider) : ALoadoutSynchronizer(provider)
{
    protected override IGamePathFilter GamePathFilter { get; } =
        GamePathFilters.Create(path =>
            !path.InFolder(OblivionRemasteredGame.PakModsPath));

    public override bool IsIgnoredBackupPath(GamePath path)
    {
        return !path.InFolder(
            OblivionRemasteredGame.PakModsPath);
    }
}
