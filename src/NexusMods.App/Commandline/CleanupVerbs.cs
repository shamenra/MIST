using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Cli;
using NexusMods.Sdk.ProxyConsole;

namespace NexusMods.App.Commandline;

/// <summary>
/// MIST-006-14A1: emergency guard against inherited destructive uninstall.
/// Uninstallation of the executable itself is just deleting the AppImage.
/// Data/archives must not be recursively purged until a path-scoped, previewed,
/// verified data-cleanup workflow is implemented and tested.
/// </summary>
internal static class CleanupVerbs
{
    internal static IServiceCollection AddCleanupVerbs(this IServiceCollection services) =>
        services.AddVerb(() => UninstallApp);

    [Verb("uninstall-app", "M.I.S.T. safe uninstall guard (game and archive deletion disabled)")]
    private static async Task<int> UninstallApp([Injected] IRenderer renderer)
    {
        await renderer.Text("M.I.S.T. safeguard: uninstall-app is blocked. No game files or archives have been modified. Delete the AppImage executable to remove the program; app data and archive deletion will require a separate verified purge command.");
        return 1;
    }
}
