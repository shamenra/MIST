using System.Text.Json;
using NexusMods.DataModel.ModDiscovery;

namespace MIST.ModRecognitionProbe;

/// <summary>Standalone read-only probe: does not start MIST, resolve archives or access the database.</summary>
internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length != 4 || args[0] != "--game-root" || args[2] != "--output")
            {
                Console.Error.WriteLine("Usage: MIST.ModRecognitionProbe --game-root CYBERPUNK_DIRECTORY --output REPORT.json");
                Console.Error.WriteLine("No --downloads-root: detection is strictly contained within the selected game's installation.");
                return Task.FromResult(2);
            }

            var root = Path.GetFullPath(args[1]);
            var output = Path.GetFullPath(args[3]);
            if (!MISTScopedCyberpunkDiscovery.IsSafeReportPath(output, root))
            {
                Console.Error.WriteLine("REFUSED: output must not be inside the game directory, including through a linked parent.");
                return Task.FromResult(2);
            }
            var report = MISTModDiscoveryPipeline.Scan(root);
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
            // Must not overwrite existing files. No output is written until scan succeeds.
            using (var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(file)) writer.Write(json);
            Console.WriteLine("M.I.S.T. game-scoped recognition completed (read-only)");
            Console.WriteLine($"Scanned mod-path files: {report.Inventory.InstalledFileCount}; structural groups: {report.Inventory.CandidateGroups.Count}");
            Console.WriteLine($"Ungrouped files: {report.Inventory.UnattributedFiles.Count}; skipped paths: {report.Inventory.SkippedPaths.Count}");
            Console.WriteLine($"Local identity hints: {report.IdentityHints.Count}; sampled fingerprints: {report.ContentFingerprints.Count}; potential relationships: {report.PossibleRelationships.Count}");
            Console.WriteLine("External archive/Downloads directories inspected: 0; mod/game files changed: 0");
            Console.WriteLine($"Report: {output}");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"M.I.S.T. recognition stopped: {ex.Message}");
            return Task.FromResult(1);
        }
    }

}
