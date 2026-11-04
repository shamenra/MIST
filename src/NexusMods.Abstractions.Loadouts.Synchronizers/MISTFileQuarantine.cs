using NexusMods.Hashing.xxHash3;
using NexusMods.Sdk.Hashes;

namespace NexusMods.Abstractions.Loadouts.Synchronizers;

/// <summary>
/// MIST-003B.5P.1: Verified, non-overwriting file quarantine.
///
/// This primitive does not establish ownership or authorization.
/// Before using it in a live synchronizer operation, the caller
/// must persist an appropriate recovery journal.
///
/// Files are moved within the same directory to avoid copying
/// large mod archives across filesystems.
///
/// No operation here is a substitute for race-resistant
/// filesystem handles or a complete recovery transaction.
/// </summary>
public static class MISTFileQuarantine
{
    public static string CreateQuarantinePath(string originalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);

        var original = Path.GetFullPath(originalPath);
        var parent = Path.GetDirectoryName(original)
            ?? throw new IOException("Original path has no parent.");

        return Path.Combine(
            parent,
            $".mist-quarantine-{Guid.NewGuid():N}");
    }

    public static async Task MoveVerifiedAsync(
        string originalPath,
        string quarantinePath,
        Hash expectedHash,
        CancellationToken cancellationToken = default)
    {
        var (original, quarantine) =
            ValidatePaths(originalPath, quarantinePath);

        EnsureOrdinaryFile(original);
        EnsureAbsent(quarantine);

        var observed = await HashFileAsync(
            original, cancellationToken);

        if (observed != expectedHash)
        {
            throw new IOException(
                "M.I.S.T. refused quarantine: file contents " +
                "do not match the verified deployment hash.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        File.Move(
            original,
            quarantine,
            overwrite: false);

        // Check the bytes at their new location.
        // If verification fails, preserve the quarantine file
        // for investigation rather than deleting anything.
        var quarantinedHash = await HashFileAsync(
            quarantine, CancellationToken.None);

        if (quarantinedHash != expectedHash)
        {
            throw new IOException(
                "Quarantined file contents changed. " +
                "Preserving the file for manual investigation.");
        }
    }

    public static async Task RestoreVerifiedAsync(
        string originalPath,
        string quarantinePath,
        Hash expectedHash,
        CancellationToken cancellationToken = default)
    {
        var (original, quarantine) =
            ValidatePaths(originalPath, quarantinePath);

        EnsureOrdinaryFile(quarantine);

        // Never overwrite a file that appeared at the
        // original destination after quarantine began.
        EnsureAbsent(original);

        var observed = await HashFileAsync(
            quarantine, cancellationToken);

        if (observed != expectedHash)
        {
            throw new IOException(
                "M.I.S.T. refused restoration: quarantined " +
                "content no longer matches the recorded hash.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        File.Move(
            quarantine,
            original,
            overwrite: false);
    }

    /// <summary>
    /// Finalizes a quarantined deletion after the corresponding
    /// database transaction has successfully committed.
    ///
    /// This method does not authorize deletion on its own.
    /// The caller must have a committed transaction and a
    /// matching persistent deletion journal.
    ///
    /// High-level path APIs cannot eliminate concurrent
    /// filesystem replacement races. Do not use this as
    /// a substitute for handle-based filesystem operations.
    /// </summary>
    public static async Task FinalizeVerifiedAsync(
        string originalPath,
        string quarantinePath,
        Hash expectedHash,
        CancellationToken cancellationToken = default)
    {
        var (original, quarantine) =
            ValidatePaths(originalPath, quarantinePath);

        // Never finalize a symbolic link, directory, or
        // file outside the validated quarantine location.
        EnsureOrdinaryFile(quarantine);

        // If someone recreated the original destination,
        // leave the quarantine intact for investigation.
        EnsureAbsent(original);

        var observed = await HashFileAsync(
            quarantine, cancellationToken);

        if (observed != expectedHash)
        {
            throw new IOException(
                "M.I.S.T. refused to finalize quarantine: " +
                "file contents differ from the recorded hash. " +
                "Preserving the file for manual review.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // All ordinary checks passed. This is still subject
        // to the path-replacement race documented above.
        File.Delete(quarantine);
    }

    private static (string Original, string Quarantine)
        ValidatePaths(
            string originalPath,
            string quarantinePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(originalPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(quarantinePath);

        var original = Path.GetFullPath(originalPath);
        var quarantine = Path.GetFullPath(quarantinePath);

        var originalParent = Path.GetDirectoryName(original);
        var quarantineParent = Path.GetDirectoryName(quarantine);

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (originalParent is null ||
            quarantineParent is null ||
            !string.Equals(
                originalParent,
                quarantineParent,
                comparison))
        {
            throw new IOException(
                "Quarantine must be adjacent to the original file.");
        }

        if (!Path.GetFileName(quarantine).StartsWith(
                ".mist-quarantine-",
                StringComparison.Ordinal))
        {
            throw new IOException(
                "Invalid M.I.S.T. quarantine filename.");
        }

        // Inspect every ancestor, not just the immediate parent.
        // This deliberately rejects symbolic-link directories.
        for (
            string? parent = originalParent;
            parent is not null;
            parent = Path.GetDirectoryName(parent))
        {
            var attributes = File.GetAttributes(parent);

            if ((attributes & FileAttributes.ReparsePoint) != 0 ||
                (attributes & FileAttributes.Directory) == 0)
            {
                throw new IOException(
                    "M.I.S.T. refused a linked or invalid " +
                    "parent directory: " + parent);
            }
        }

        return (original, quarantine);
    }

    private static void EnsureOrdinaryFile(string path)
    {
        var attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.ReparsePoint) != 0 ||
            (attributes & FileAttributes.Directory) != 0)
        {
            throw new IOException(
                "M.I.S.T. requires an ordinary, non-linked file.");
        }
    }

    private static void EnsureAbsent(string path)
    {
        try
        {
            _ = File.GetAttributes(path);

            throw new IOException(
                "M.I.S.T. refuses to overwrite an existing path: " +
                path);
        }
        catch (FileNotFoundException)
        {
            // No entry found. File.Move still independently
            // refuses to overwrite an existing destination.
        }
    }

    private static async Task<Hash> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        EnsureOrdinaryFile(path);

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        return (await MultiHasher.HashStream(
            stream, cancellationToken)).XxHash3;
    }
}
