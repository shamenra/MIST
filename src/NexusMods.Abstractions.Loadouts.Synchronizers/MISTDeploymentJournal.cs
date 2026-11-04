using System.Text.Json;
using NexusMods.Hashing.xxHash3;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// A record of a deployment that may not have committed.
///
/// A journal is NOT an ownership receipt or authorization
/// to modify any file.
/// </summary>
public sealed record MISTPendingDeployment(
    Guid OperationId,
    DateTimeOffset CreatedUtc,
    string InstallationEntityId,
    MISTPendingDeploymentFile[] Files);

public sealed record MISTPendingDeploymentFile(
    string StagedPath,
    string DestinationPath,
    string ExpectedHash);

/// <summary>
/// MIST-003B.5K: Persistent write-ahead deployment records.
///
/// The caller must create a pending record before publication
/// and complete it only after the database commit succeeds.
///
/// Recovery decisions belong to a separate verified workflow.
/// </summary>
public static class MISTDeploymentJournal
{
    /// <summary>
    /// Uses XDG state storage on Unix-like systems and local
    /// application data on Windows.
    ///
    /// Never stores recovery records inside a system directory.
    /// </summary>
    public static string GetDefaultDirectory()
    {
        string root;

        if (OperatingSystem.IsWindows())
        {
            root = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
        }
        else
        {
            var configured =
                Environment.GetEnvironmentVariable(
                    "XDG_STATE_HOME");

            root = !string.IsNullOrWhiteSpace(configured) &&
                   Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.UserProfile),
                    ".local",
                    "state");
        }

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(
                "M.I.S.T. could not determine a per-user " +
                "deployment journal directory.");
        }

        return Path.Combine(
            root,
            "MIST",
            "deployment-journal");
    }

    public static async Task<string> BeginAsync(
        string journalDirectory,
        string installationEntityId,
        IReadOnlyList<(
            string StagedPath,
            string DestinationPath,
            Hash ExpectedHash)> files,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            journalDirectory);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            installationEntityId);
        ArgumentNullException.ThrowIfNull(files);

        if (files.Count == 0)
        {
            throw new ArgumentException(
                "Cannot journal an empty deployment.",
                nameof(files));
        }

        // Validate all entries before creating anything.
        var entries = files.Select(file =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                file.StagedPath);

            ArgumentException.ThrowIfNullOrWhiteSpace(
                file.DestinationPath);

            return new MISTPendingDeploymentFile(
                Path.GetFullPath(file.StagedPath),
                Path.GetFullPath(file.DestinationPath),
                file.ExpectedHash.ToHex());
        }).ToArray();

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetFullPath(journalDirectory);
        Directory.CreateDirectory(directory);

        var operationId = Guid.NewGuid();

        var record = new MISTPendingDeployment(
            operationId,
            DateTimeOffset.UtcNow,
            installationEntityId,
            entries);

        var prefix = operationId.ToString("N");

        var temporaryPath = Path.Combine(
            directory, prefix + ".writing");

        var pendingPath = Path.Combine(
            directory, prefix + ".pending.json");

        // Write a complete record before making it visible
        // as a pending deployment.
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

            // Request a flush of the file's data to storage.
            stream.Flush(flushToDisk: true);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Publication must not begin until this succeeds.
        File.Move(
            temporaryPath,
            pendingPath,
            overwrite: false);

        return pendingPath;
    }

    /// <summary>
    /// Reads unfinished operation records without changing
    /// any deployed files.
    ///
    /// Malformed records cause an error rather than being
    /// silently discarded.
    /// </summary>
    public static IReadOnlyList<(
        string Path,
        MISTPendingDeployment Record)> ReadPending(
        string journalDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            journalDirectory);

        if (!Directory.Exists(journalDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(
                journalDirectory,
                "*.pending.json")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(path =>
            {
                var record =
                    JsonSerializer.Deserialize<
                        MISTPendingDeployment>(
                        File.ReadAllText(path));

                if (record is null)
                {
                    throw new InvalidDataException(
                        "Invalid M.I.S.T. journal: " + path);
                }

                return (Path: path, Record: record);
            })
            .ToArray();
    }

    /// <summary>
    /// Call ONLY after the associated database transaction
    /// has committed successfully.
    ///
    /// Never call this merely because publication succeeded.
    /// </summary>
    public static void Complete(string pendingPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            pendingPath);

        File.Delete(pendingPath);
    }
}
