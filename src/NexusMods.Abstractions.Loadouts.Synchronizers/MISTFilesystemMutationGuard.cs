using NexusMods.Abstractions.Loadouts.Synchronizers.Rules;
using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Games;
using NexusMods.Sdk.Hashes;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// MIST-003B.5C: Bridges planned mutations to actual
/// filesystem observations and persistent ownership evidence.
///
/// Does not modify files. Checks must also be enforced at
/// execution time to address changes after this preflight.
/// </summary>
public static class MISTFilesystemMutationGuard
{
    public static async Task EnsureAuthorizedAsync(
        IReadOnlyDictionary<GamePath, SyncNode> plan,
        Func<GamePath, string> resolveDestination,
        Func<GamePath, Hash, bool> hasVerifiedReceipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(resolveDestination);
        ArgumentNullException.ThrowIfNull(hasVerifiedReceipt);

        var existingFiles = new Dictionary<GamePath, bool>();
        var verifiedFiles = new HashSet<GamePath>();

        foreach (var (path, node) in plan)
        {
            var deletes = node.Actions.HasFlag(
                Actions.DeleteFromDisk);

            var writes =
                node.Actions.HasFlag(Actions.ExtractToDisk) ||
                node.Actions.HasFlag(Actions.WriteIntrinsic);

            if (!deletes && !writes)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            var destination = resolveDestination(path);

            // MIST-003B.5E: Prevent parent-directory links from
            // redirecting mutations outside the intended location.
            EnsureNoLinkedParentDirectories(destination, path);

            // File.Exists alone is not sufficient because it
            // can return false for inaccessible paths.
            // Only genuine absence is treated as a new file.
            var exists = false;

            try
            {
                var attributes = File.GetAttributes(destination);

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    throw new InvalidOperationException(
                        $"M.I.S.T. refuses to modify a directory " +
                        $"at '{path.Path}'.");
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"M.I.S.T. refuses to modify a symbolic link " +
                        $"at '{path.Path}'.");
                }

                exists = true;
            }
            catch (FileNotFoundException)
            {
                // The destination is absent.
            }
            catch (DirectoryNotFoundException)
            {
                // The destination's parent does not yet exist.
            }

            existingFiles.Add(path, exists);

            // MIST-003B.5G:
            // Existing-file replacement is not yet recoverable.
            // Reject it before the action loop can delete the old file.
            // A valid receipt is evidence, not permission to overwrite.
            if (exists && writes)
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. refuses to replace '{path.Path}': " +
                    "safe existing-file writes are not yet supported.");
            }

            if (!exists)
                continue;

            // Read the actual file, not its cached disk-state hash
            // or a hash inferred from Steam metadata.
            await using var stream = File.OpenRead(destination);

            var hashes = await MultiHasher.HashStream(
                stream,
                cancellationToken);

            if (hasVerifiedReceipt(path, hashes.XxHash3))
                verifiedFiles.Add(path);
        }

        // Apply the existing fail-closed mutation policy
        // after collecting filesystem observations.
        MISTMutationPreflight.EnsureAuthorized(
            plan,
            path => existingFiles[path],
            path => verifiedFiles.Contains(path));
    }

    /// <summary>
    /// Rejects linked or invalid parent directories. This is a
    /// preflight check, not protection against concurrent path changes.
    /// </summary>
    private static void EnsureNoLinkedParentDirectories(
        string destination,
        GamePath gamePath)
    {
        var parent = Path.GetDirectoryName(
            Path.GetFullPath(destination));

        while (parent is not null)
        {
            FileAttributes attributes;

            try
            {
                attributes = File.GetAttributes(parent);
            }
            catch (FileNotFoundException)
            {
                // New deployment directories may not exist yet.
                parent = Path.GetDirectoryName(parent);
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                parent = Path.GetDirectoryName(parent);
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. refuses to modify '{gamePath.Path}': " +
                    $"a parent directory is a symbolic link " +
                    $"or reparse point: '{parent}'.");
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. refuses to modify '{gamePath.Path}': " +
                    $"a parent path is not a directory: '{parent}'.");
            }

            parent = Path.GetDirectoryName(parent);
        }
    }
}
