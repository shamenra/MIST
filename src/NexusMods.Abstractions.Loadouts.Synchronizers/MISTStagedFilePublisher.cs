using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// Publishes verified staged content without overwriting an existing file.
/// The caller must stage content on the destination filesystem.
/// </summary>
public static class MISTStagedFilePublisher
{
    /// <summary>
    /// Validates every staged file and destination before publishing
    /// anything. Publication can still partially fail due to I/O,
    /// concurrent filesystem changes, or process interruption.
    /// </summary>
    public static async Task PublishNewFilesAsync(
        IReadOnlyList<(string StagedPath, string DestinationPath, Hash ExpectedHash)> files,
        CancellationToken cancellationToken = default,
        Action<(int Current, int Max)>? progressUpdater = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        var destinations = new HashSet<string>(comparer);
        var stagedPaths = new HashSet<string>(comparer);

        // Phase 1: Validate the entire batch without publishing.
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ArgumentException.ThrowIfNullOrWhiteSpace(file.StagedPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(file.DestinationPath);

            var staged = Path.GetFullPath(file.StagedPath);
            var destination = Path.GetFullPath(file.DestinationPath);

            if (comparer.Equals(staged, destination))
            {
                throw new InvalidOperationException(
                    "M.I.S.T. staged path equals its destination.");
            }

            if (!stagedPaths.Add(staged))
            {
                throw new InvalidOperationException(
                    "M.I.S.T. batch contains duplicate staged paths.");
            }

            if (!destinations.Add(destination))
            {
                throw new InvalidOperationException(
                    "M.I.S.T. batch contains duplicate destinations.");
            }

            if (File.Exists(destination) ||
                Directory.Exists(destination))
            {
                throw new IOException(
                    "M.I.S.T. destination already exists: " +
                    destination);
            }

            await using var stream = File.OpenRead(staged);

            var hashes = await MultiHasher.HashStream(
                stream, cancellationToken);

            if (hashes.XxHash3 != file.ExpectedHash)
            {
                throw new InvalidOperationException(
                    "M.I.S.T. staged file hash mismatch; " +
                    "batch publication was not started.");
            }
        }

        // Phase 2: Track successful publications so failures
        // can trigger best-effort recovery.
        var published = new List<(
            string StagedPath,
            string DestinationPath,
            Hash ExpectedHash)>();

        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await PublishNewFileAsync(
                    file.StagedPath,
                    file.DestinationPath,
                    file.ExpectedHash,
                    cancellationToken);

                published.Add(file);

                progressUpdater?.Invoke(
                    (published.Count, files.Count));
            }
        }
        catch (Exception publicationError)
        {
            var recoveryErrors = new List<Exception>();

            // Reverse successful publications.
            for (var index = published.Count - 1;
                 index >= 0;
                 index--)
            {
                var file = published[index];

                try
                {
                    var attributes =
                        File.GetAttributes(file.DestinationPath);

                    if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                        (attributes & FileAttributes.Directory) != 0)
                    {
                        throw new IOException(
                            "Published destination is no longer " +
                            "an ordinary file.");
                    }

                    // Never recover content that was modified
                    // after M.I.S.T. published it.
                    await using (var stream =
                        File.OpenRead(file.DestinationPath))
                    {
                        var hashes =
                            await MultiHasher.HashStream(stream);

                        if (hashes.XxHash3 != file.ExpectedHash)
                        {
                            throw new IOException(
                                "Published file changed after deployment. " +
                                "Preserving it for manual review.");
                        }
                    }

                    // Move back without overwriting anything.
                    File.Move(
                        file.DestinationPath,
                        file.StagedPath,
                        overwrite: false);
                }
                catch (Exception recoveryError)
                {
                    recoveryErrors.Add(new IOException(
                        "M.I.S.T. could not safely recover '" +
                        file.DestinationPath +
                        "'. Manual review is required.",
                        recoveryError));
                }
            }

            if (recoveryErrors.Count > 0)
            {
                recoveryErrors.Insert(0, publicationError);

                throw new AggregateException(
                    "M.I.S.T. publication failed and partial " +
                    "deployment may remain; manual review required.",
                    recoveryErrors);
            }

            throw;
        }
    }

    public static async Task PublishNewFileAsync(
        string stagedPath,
        string destinationPath,
        Hash expectedHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        // Hash the actual staged bytes, not cached installation metadata.
        await using (var stream = System.IO.File.OpenRead(stagedPath))
        {
            var hashes = await MultiHasher.HashStream(
                stream, cancellationToken);

            if (hashes.XxHash3 != expectedHash)
            {
                throw new InvalidOperationException(
                    "M.I.S.T. staged file hash mismatch; " +
                    "destination was not published.");
            }
        }

        // Never replace content that appeared after planning.
        System.IO.File.Move(
            stagedPath,
            destinationPath,
            overwrite: false);
    }
}
