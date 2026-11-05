using NexusMods.Games.OblivionRemastered;
using NexusMods.Sdk.Games;
using Xunit;

namespace NexusMods.DataModel.Tests;

public class MISTOblivionRemasteredTests
{
    [Fact]
    public void MISTGameHasIndependentIdentityAndSteamId()
    {
        Assert.Equal(
            GameId.From("MIST.OblivionRemastered"),
            OblivionRemasteredGame.GameId);

        Assert.Equal(
            2623190u,
            OblivionRemasteredGame.SteamAppId);

        Assert.True(
            OblivionRemasteredGame.NexusModsGameId.HasValue);
    }

    [Fact]
    public void MISTPakDestinationUsesRemasteredLayout()
    {
        Assert.Equal(
            "OblivionRemastered/Content/Paks/~mods",
            OblivionRemasteredGame.PakModsPath.Path.ToString());
    }
}
