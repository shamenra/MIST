using System.Globalization;
using DynamicData.Kernel;
using NexusMods.Abstractions.Games.FileHashes.Models;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.MnemonicDB.Abstractions;
using NexusMods.Sdk;
using NexusMods.Sdk.Games;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Describes recognition of installed Steam depot manifest IDs.
///
/// AllKnown means every reported identifier is present in the
/// queried data source. It does not establish that local game
/// files are pristine or that manifest file records are complete.
/// </summary>
public enum SteamManifestCoverageStatus
{
    MissingMetadata,
    InvalidMetadata,
    NoneKnown,
    Partial,
    AllKnown,
}

public sealed record SteamManifestCoverage(
    SteamManifestCoverageStatus Status,
    int ManifestCount,
    int KnownCount,
    IReadOnlyList<ulong> MissingManifestIds)
{
    public bool AllInstalledManifestsKnown =>
        Status == SteamManifestCoverageStatus.AllKnown;
}

/// <summary>
/// Evaluates installed Steam depot manifest identifier coverage.
/// Performs no filesystem modifications or network requests.
/// </summary>
public static class SteamManifestCoverageEvaluator
{
    public static SteamManifestCoverage Evaluate(
        IEnumerable<string>? installedManifestIds,
        Func<ulong, bool> isKnownManifest)
    {
        ArgumentNullException.ThrowIfNull(isKnownManifest);

        if (installedManifestIds is null)
            return MissingMetadata();

        var identifiers = installedManifestIds.ToArray();

        if (identifiers.Length == 0)
            return MissingMetadata();

        var parsedIds = new HashSet<ulong>();

        foreach (var identifier in identifiers)
        {
            if (!ulong.TryParse(
                    identifier,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var manifestId) ||
                manifestId == 0)
            {
                return new SteamManifestCoverage(
                    SteamManifestCoverageStatus.InvalidMetadata,
                    0,
                    0,
                    Array.Empty<ulong>());
            }

            parsedIds.Add(manifestId);
        }

        var missing = parsedIds
            .Where(id => !isKnownManifest(id))
            .OrderBy(id => id)
            .ToArray();

        var recognized = parsedIds.Count - missing.Length;

        var status = recognized switch
        {
            0 => SteamManifestCoverageStatus.NoneKnown,
            _ when missing.Length == 0 =>
                SteamManifestCoverageStatus.AllKnown,
            _ => SteamManifestCoverageStatus.Partial,
        };

        return new SteamManifestCoverage(
            status,
            parsedIds.Count,
            recognized,
            missing);
    }

    /// <summary>
    /// Evaluates installed Steam manifest identifiers against an
    /// existing game-hash database.
    ///
    /// This is an identifier lookup only. Recognized manifests are
    /// not necessarily complete, pristine, or appropriate for reset.
    ///
    /// The database must already be initialized by the caller.
    /// </summary>
    public static SteamManifestCoverage EvaluateDatabase(
        IEnumerable<LocatorId>? installedManifestIds,
        IDb database)
    {
        ArgumentNullException.ThrowIfNull(database);

        return Evaluate(
            installedManifestIds?.Select(id => id.Value),
            manifestId =>
                SteamManifest
                    .FindByManifestId(
                        database,
                        ManifestId.From(manifestId))
                    .TryGetFirst(out _));
    }

    private static SteamManifestCoverage MissingMetadata() =>
        new(
            SteamManifestCoverageStatus.MissingMetadata,
            0,
            0,
            Array.Empty<ulong>());
}
