using System.Text.Json;
using NexusMods.Hashing.xxHash3;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// Evidence of an attempted managed-file deletion.
///
/// This is NOT proof of ownership or permission to delete.
/// </summary>
public sealed record MISTPendingDeletionRecord(
    Guid OperationId,
    DateTimeOffset CreatedUtc,
    string InstallationEntityId,
    string ReceiptEntityId,
    string OriginalPath,
    string QuarantinePath,
    string ExpectedHash);

public sealed record MISTPendingDeletionHandle(
    string JournalPath,
    string OriginalPath,
    string QuarantinePath,
    Hash ExpectedHash);

/// <summary>
/// MIST-003B.5P.3: Write-ahead deletion records.
///
/// Records are created before quarantine moves and completed
/// only after a successful database commit and verified
/// quarantine finalization.
///
/// This class does not provide automatic crash recovery.
/// </summary>
public static class MISTDeletionJournal
{
    public static async Task<MISTPendingDeletionHandle> BeginAsync(
        string journalDirectory,
        string installationEntityId,
        string receiptEntityId,
        string originalPath,
        string quarantinePath,
        Hash expectedHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            journalDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            installationEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            receiptEntityId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            quarantinePath);

        if (!Path.IsPathFullyQualified(originalPath) ||
            !Path.IsPathFullyQualified(quarantinePath))
        {
            throw new IOException(
                "Deletion journal requires absolute paths.");
        }

        var original = Path.GetFullPath(originalPath);
        var quarantine = Path.GetFullPath(quarantinePath);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!string.Equals(
                Path.GetDirectoryName(original),
                Path.GetDirectoryName(quarantine),
                comparison) ||
            !Path.GetFileName(quarantine).StartsWith(
                ".mist-quarantine-",
                StringComparison.Ordinal))
        {
            throw new IOException(
                "Deletion quarantine must be adjacent " +
                "to the original file.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetFullPath(journalDirectory);
        Directory.CreateDirectory(directory);

        var operationId = Guid.NewGuid();

        var record = new MISTPendingDeletionRecord(
            operationId,
            DateTimeOffset.UtcNow,
            installationEntityId,
            receiptEntityId,
            original,
            quarantine,
            expectedHash.ToHex());

        var prefix = operationId.ToString("N");

        var temporaryPath = Path.Combine(
            directory,
            prefix + ".writing-delete");

        var pendingPath = Path.Combine(
            directory,
            prefix + ".pending-delete.json");

        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                record,
                cancellationToken: cancellationToken);

            stream.Flush(flushToDisk: true);
        }

        cancellationToken.ThrowIfCancellationRequested();

        File.Move(
            temporaryPath,
            pendingPath,
            overwrite: false);

        return new MISTPendingDeletionHandle(
            pendingPath,
            original,
            quarantine,
            expectedHash);
    }

    /// <summary>
    /// Loads unfinished deletion records without modifying
    /// files or attempting restoration.
    /// </summary>
    public static IReadOnlyList<(
        string Path,
        MISTPendingDeletionRecord Record)> ReadPending(
        string journalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            journalDirectory);

        if (!Directory.Exists(journalDirectory))
            return [];

        return Directory.EnumerateFiles(
                journalDirectory,
                "*.pending-delete.json")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(path =>
            {
                var record =
                    JsonSerializer.Deserialize<
                        MISTPendingDeletionRecord>(
                        File.ReadAllText(path));

                if (record is null ||
                    record.OperationId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(
                        record.InstallationEntityId) ||
                    string.IsNullOrWhiteSpace(
                        record.ReceiptEntityId) ||
                    string.IsNullOrWhiteSpace(
                        record.OriginalPath) ||
                    string.IsNullOrWhiteSpace(
                        record.QuarantinePath) ||
                    string.IsNullOrWhiteSpace(
                        record.ExpectedHash))
                {
                    throw new InvalidDataException(
                        "Invalid M.I.S.T. deletion journal: " +
                        path);
                }

                return (Path: path, Record: record);
            })
            .ToArray();
    }

    /// <summary>
    /// This method must only be called AFTER the caller's
    /// database transaction has committed successfully.
    ///
    /// If cleanup fails, leave the journal in place.
    /// </summary>
    public static async Task FinalizeCommittedAsync(
        IReadOnlyList<MISTPendingDeletionHandle> deletions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deletions);

        foreach (var deletion in deletions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await MISTFileQuarantine.FinalizeVerifiedAsync(
                deletion.OriginalPath,
                deletion.QuarantinePath,
                deletion.ExpectedHash,
                cancellationToken);

            // Finalize succeeded. This operation's pending
            // journal can now be retired.
            File.Delete(deletion.JournalPath);
        }
    }
}
