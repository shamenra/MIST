namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// Read-only summary of unresolved deployment and deletion operations.
/// An unresolved journal does NOT indicate whether the database committed.
/// </summary>
public sealed record MISTPendingOperationAssessment(
    int Deployments,
    int Deletions)
{
    public int Total => checked(Deployments + Deletions);
    public bool RequiresReview => Total > 0;
}

/// <summary>
/// MIST-14D: Fail closed if the same installation has a pending filesystem
/// operation from a previous synchronization attempt.
///
/// Never restores, deletes, retries, or marks a journal complete. A matching
/// filesystem hash is not proof of ownership or a committed database tx.
/// </summary>
public static class MISTPendingOperationGate
{
    public static MISTPendingOperationAssessment Assess(
        string journalDirectory,
        string installationEntityId,
        string? alternateInstallationEntityId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(installationEntityId);

        // The existing readers are read-only. Invalid JSON will throw, which
        // intentionally prevents authorization rather than silently passing.
        var deployments = MISTDeploymentJournal.ReadPending(journalDirectory);
        var deletions = MISTDeletionJournal.ReadPending(journalDirectory);

        // Reject malformed deployment records even when they can't reliably
        // be assigned to an installation. We cannot safely ignore them.
        foreach (var (path, record) in deployments)
        {
            if (record.OperationId == Guid.Empty ||
                string.IsNullOrWhiteSpace(record.InstallationEntityId) ||
                record.Files is null || record.Files.Length == 0 ||
                record.Files.Any(file =>
                    file is null ||
                    string.IsNullOrWhiteSpace(file.StagedPath) ||
                    string.IsNullOrWhiteSpace(file.DestinationPath) ||
                    string.IsNullOrWhiteSpace(file.ExpectedHash) ||
                    !Path.IsPathFullyQualified(file.StagedPath) ||
                    !Path.IsPathFullyQualified(file.DestinationPath)))
            {
                throw new InvalidDataException(
                    "M.I.S.T. refuses synchronization: malformed " +
                    "pending deployment journal: " + path);
            }
        }

        // Older call sites may stringify either the typed installation id
        // or its underlying EntityId. Both representations are accepted.
        bool IsSameInstallation(string recordedId) =>
            string.Equals(recordedId, installationEntityId, StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(alternateInstallationEntityId) &&
             string.Equals(recordedId, alternateInstallationEntityId, StringComparison.Ordinal));

        var pendingDeployments = deployments.Count(entry =>
            IsSameInstallation(entry.Record.InstallationEntityId));

        var pendingDeletions = deletions.Count(entry =>
            IsSameInstallation(entry.Record.InstallationEntityId));

        return new MISTPendingOperationAssessment(
            pendingDeployments,
            pendingDeletions);
    }

    /// <summary>
    /// Blocks new file mutations for this installation until its unresolved
    /// recovery journals have been safely reconciled by a separate process.
    /// </summary>
    public static void EnsureClear(
        string journalDirectory,
        string installationEntityId,
        string? alternateInstallationEntityId = null)
    {
        var assessment = Assess(
            journalDirectory, installationEntityId, alternateInstallationEntityId);
        if (!assessment.RequiresReview) return;

        throw new InvalidOperationException(
            $"M.I.S.T. refuses to synchronize installation " +
            $"'{installationEntityId}': " +
            $"{assessment.Deployments} unresolved deployment journal(s) " +
            $"and {assessment.Deletions} unresolved deletion journal(s). " +
            "The database commit outcome may be ambiguous. " +
            "Preserve the journal and files for recovery review.");
    }
}
