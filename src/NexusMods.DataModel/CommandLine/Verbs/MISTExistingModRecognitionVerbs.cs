using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NexusMods.Abstractions.Cli;
using NexusMods.DataModel.ModDiscovery;
using NexusMods.Sdk.ProxyConsole;

namespace NexusMods.DataModel.CommandLine.Verbs;

/// <summary>Game-scoped, read-only discovery. No loadout sync, database write or external archive scan.</summary>
internal static class MISTExistingModRecognitionVerbs
{
    internal static IServiceCollection AddMISTExistingModRecognitionVerbs(this IServiceCollection services) =>
        services.AddModule("mist", "M.I.S.T. development and read-only diagnostics")
            .AddVerb(() => RecognizeCyberpunk);

    [Verb("mist recognize-cyberpunk", "Read-only installed-mod grouping strictly within a Cyberpunk installation")]
    private static async Task<int> RecognizeCyberpunk(
        [Injected] IRenderer renderer,
        [Option("g", "game-root", "Cyberpunk 2077 installation directory")] string gameRoot,
        [Option("o", "output", "JSON report path outside the game directory")] string output,
        [Injected] CancellationToken cancellationToken)
    {
        var outputPath = Path.GetFullPath(output);
        var root = Path.GetFullPath(gameRoot);
        if (!MISTScopedCyberpunkDiscovery.IsSafeReportPath(outputPath, root))
        {
            await renderer.TextLine("REFUSED: report must be outside the selected game directory.");
            return 2;
        }
        var report = MISTModDiscoveryPipeline.Scan(root, cancellationToken);
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        await using (var file = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var writer = new StreamWriter(file))
            await writer.WriteAsync(json.AsMemory(), cancellationToken);
        await renderer.TextLine("M.I.S.T. game-scoped recognition finished.");
        await renderer.TextLine("Game mod-path files: {0}; structural candidates: {1}; unattributed: {2}",
            report.Inventory.InstalledFileCount, report.Inventory.CandidateGroups.Count, report.Inventory.UnattributedFiles.Count);
        await renderer.TextLine("Game-local identity hints: {0}; sampled file fingerprints: {1}; possible component relationships: {2}",
            report.IdentityHints.Count, report.ContentFingerprints.Count, report.PossibleRelationships.Count);
        await renderer.TextLine("External downloads and other games were not scanned. Report: {0}", outputPath);
        return 0;
    }
}
