using System.Reflection;
using NexusMods.Paths;
using Xunit;

namespace NexusMods.Networking.Steam.Tests;

public class MISTSteamAuthStorageTests
{
    [Fact]
    public void AuthenticationUsesIndependentApplicationDirectory()
    {
        var fileSystem = FileSystem.Shared;

        var type = typeof(Session).Assembly.GetType(
            "NexusMods.Networking.Steam.AppDirectoryAuthStorage",
            throwOnError: true)!;

        var instance = Activator.CreateInstance(
            type,
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic,
            binder: null,
            args: [fileSystem],
            culture: null);

        Assert.NotNull(instance);

        var field = type.GetField(
            "_storagePath",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);

        var actual = (AbsolutePath)field.GetValue(instance)!;

        var root = OperatingSystem.IsLinux()
            ? KnownPath.XDG_DATA_HOME
            : KnownPath.LocalApplicationDataDirectory;

        var directory = OperatingSystem.IsMacOS()
            ? "MIST_App"
            : "ModIntegrationAndSortingTechnology";

        var expected = fileSystem.GetKnownPath(root)
            .Combine(directory)
            .Combine("steam/auth");

        Assert.Equal(expected.ToString(), actual.ToString());
    }
}
