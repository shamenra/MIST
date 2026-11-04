using System.Globalization;
using NexusMods.Abstractions.Games.FileHashes.Models;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// MIST-003: Conservative Steam original-state reset preflight.
///
/// Rejects missing, inconsistent, or obviously unusable
/// manifest metadata before a baseline-dependent reset.
///
/// This is not a complete file provenance verification.
/// </summary>
public static class SteamResetPreflight
{
    /// <summary>
    /// MIST-003B: Returns an informational warning when installed Steam
    /// manifest metadata cannot pass the existing reset preflight.
    ///
    /// This is advisory only. Missing manifest metadata does not mean
    /// the game is absent or that ordinary mod management must stop.
    ///
    /// A null result does NOT establish mod compatibility or prove
    /// that the installation is a pristine original-game baseline.
    /// </summary>
    public static string? GetManagementAdvisory(
        GameStore store,
        string storeIdentifier,
        IEnumerable<LocatorId> locatorIds,
        IDb database)
    {
        if (store != GameStore.Steam)
            return null;

        try
        {
            EnsureCanReset(
                store,
                storeIdentifier,
                locatorIds,
                database);

            return null;
        }
        catch (InvalidOperationException exception)
        {
            return
                "M.I.S.T. cannot confirm this installation's Steam " +
                "manifest metadata. The game may have been updated, " +
                "moved, or modified. Installed mods may be incompatible. " +
                "Reason: " + exception.Message;
        }
    }

    public static void EnsureCanReset(
        GameStore store,
        string storeIdentifier,
        IEnumerable<LocatorId> locatorIds,
        IDb database)
    {
        // Preserve existing behavior for non-Steam stores.
        if (store != GameStore.Steam)
            return;

        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(locatorIds);

        if (!uint.TryParse(
                storeIdentifier,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var appId) ||
            appId == 0)
        {
            throw new InvalidOperationException(
                "M.I.S.T. cannot reset this Steam installation: " +
                "the Steam application ID is invalid.");
        }

        var ids = locatorIds.ToArray();

        var coverage =
            SteamManifestCoverageEvaluator.EvaluateDatabase(
                ids,
                database);

        if (!coverage.AllInstalledManifestsKnown)
        {
            throw new InvalidOperationException(
                "M.I.S.T. cannot reset this Steam installation: " +
                $"manifest coverage is {coverage.Status}. " +
                "Missing manifest IDs: " +
                string.Join(", ", coverage.MissingManifestIds));
        }

        foreach (var id in ids.Select(x => x.Value).Distinct())
        {
            // Coverage evaluation has already validated the IDs.
            var manifestId = ManifestId.From(
                ulong.Parse(id, CultureInfo.InvariantCulture));

            var records = SteamManifest
                .FindByManifestId(database, manifestId)
                .ToArray();

            // An ambiguous association is not safe to guess.
            if (records.Length != 1)
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. cannot reset: manifest {id} " +
                    "has an ambiguous database association.");
            }

            var manifest = records[0];

            if (manifest.AppId.Value.ToString(
                    CultureInfo.InvariantCulture) !=
                appId.ToString(CultureInfo.InvariantCulture))
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. cannot reset: manifest {id} " +
                    $"does not belong to Steam app {appId}.");
            }

            // A recognized manifest with no file records
            // cannot provide an original-file baseline.
            if (!manifest.Files.Any())
            {
                throw new InvalidOperationException(
                    $"M.I.S.T. cannot reset: manifest {id} " +
                    "contains no original-file records.");
            }
        }
    }
}
