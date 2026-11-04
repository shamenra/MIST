using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

public enum MISTPendingDestinationState
{
    Absent,
    MatchesRecordedBytes,
    DiffersFromRecordedBytes,
    UnsafePath,
    CannotInspect,
}

public sealed record MISTPendingDeploymentObservation(
    string JournalPath,
    Guid OperationId,
    string InstallationEntityId,
    string DestinationPath,
    MISTPendingDestinationState State);

/// <summary>
/// MIST-003B.5M: Read-only inspection of unfinished deployments.
///
/// A matching hash is not ownership evidence.
/// This component never modifies or removes files.
/// </summary>
public static class MISTDeploymentInspector
{
    public static async Task<IReadOnlyList<MISTPendingDeploymentObservation>>
        InspectAsync(
            string journalDirectory,
            CancellationToken cancellationToken = default)
    {
        var observations =
            new List<MISTPendingDeploymentObservation>();

        foreach (var (journalPath, record) in
                 MISTDeploymentJournal.ReadPending(journalDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (record.OperationId == Guid.Empty ||
                string.IsNullOrWhiteSpace(record.InstallationEntityId) ||
                record.Files is null ||
                record.Files.Length == 0)
            {
                throw new InvalidDataException(
                    $"Invalid M.I.S.T. deployment journal: {journalPath}");
            }

            foreach (var file in record.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(file.DestinationPath) ||
                    string.IsNullOrWhiteSpace(file.StagedPath) ||
                    string.IsNullOrWhiteSpace(file.ExpectedHash) ||
                    !Path.IsPathFullyQualified(file.DestinationPath) ||
                    !Path.IsPathFullyQualified(file.StagedPath))
                {
                    throw new InvalidDataException(
                        $"Invalid M.I.S.T. deployment entry: {journalPath}");
                }

                var state = await InspectDestinationAsync(
                    file.DestinationPath,
                    file.ExpectedHash,
                    cancellationToken);

                observations.Add(new MISTPendingDeploymentObservation(
                    journalPath,
                    record.OperationId,
                    record.InstallationEntityId,
                    file.DestinationPath,
                    state));
            }
        }

        return observations;
    }

    private static async Task<MISTPendingDestinationState>
        InspectDestinationAsync(
            string destination,
            string expectedHash,
            CancellationToken cancellationToken)
    {
        try
        {
            // Do not follow linked parent directories while
            // inspecting a destination.
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
                    parent = Path.GetDirectoryName(parent);
                    continue;
                }
                catch (DirectoryNotFoundException)
                {
                    parent = Path.GetDirectoryName(parent);
                    continue;
                }

                if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                    (attributes & FileAttributes.Directory) == 0)
                {
                    return MISTPendingDestinationState.UnsafePath;
                }

                parent = Path.GetDirectoryName(parent);
            }

            FileAttributes destinationAttributes;

            try
            {
                destinationAttributes =
                    File.GetAttributes(destination);
            }
            catch (FileNotFoundException)
            {
                return MISTPendingDestinationState.Absent;
            }
            catch (DirectoryNotFoundException)
            {
                return MISTPendingDestinationState.Absent;
            }

            if ((destinationAttributes & FileAttributes.ReparsePoint) != 0 ||
                (destinationAttributes & FileAttributes.Directory) != 0)
            {
                return MISTPendingDestinationState.UnsafePath;
            }

            // Hash actual bytes. Never trust timestamps or
            // cached Steam/Nexus metadata for this observation.
            await using var stream = File.OpenRead(destination);

            var hashes = await MultiHasher.HashStream(
                stream, cancellationToken);

            return string.Equals(
                hashes.XxHash3.ToHex(),
                expectedHash,
                StringComparison.OrdinalIgnoreCase)
                ? MISTPendingDestinationState.MatchesRecordedBytes
                : MISTPendingDestinationState.DiffersFromRecordedBytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return MISTPendingDestinationState.CannotInspect;
        }
        catch (UnauthorizedAccessException)
        {
            return MISTPendingDestinationState.CannotInspect;
        }
    }
}
