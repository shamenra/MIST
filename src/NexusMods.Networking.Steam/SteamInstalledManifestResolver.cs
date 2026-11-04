using System.Globalization;
using NexusMods.Abstractions.Steam;
using NexusMods.Abstractions.Steam.DTOs;
using NexusMods.Abstractions.Steam.Values;
using NexusMods.Sdk.Games;

namespace NexusMods.Networking.Steam;

/// <summary>
/// MIST-004B: Resolves installed Steam depot manifests using
/// the existing Steam session.
///
/// This component retrieves metadata only. It does not modify
/// game files or update the game's original-file baseline.
/// </summary>
public static class SteamInstalledManifestResolver
{
    public static async Task<Manifest> ResolveAsync(
        ISteamSession session,
        GameLocatorResult installation,
        SteamInstalledDepot installedDepot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(installation);

        if (installation.Store != GameStore.Steam)
        {
            throw new InvalidOperationException(
                "Manifest resolution requires a Steam installation.");
        }

        if (!uint.TryParse(
                installation.StoreIdentifier,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var rawAppId) || rawAppId == 0)
        {
            throw new InvalidOperationException(
                "The Steam application ID is invalid.");
        }

        if (installedDepot.DepotId == 0 ||
            installedDepot.ManifestId == 0)
        {
            throw new InvalidOperationException(
                "The installed depot metadata is invalid.");
        }

        // Only resolve a depot reported by the installed-game locator.
        if (installation.SteamInstalledDepots.IsDefaultOrEmpty ||
            !installation.SteamInstalledDepots.Contains(installedDepot))
        {
            throw new InvalidOperationException(
                "The requested depot is not associated with " +
                "this installed Steam game.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var appId = AppId.From(rawAppId);
        var depotId = DepotId.From(installedDepot.DepotId);
        var manifestId = ManifestId.From(installedDepot.ManifestId);

        // Retrieve the current product metadata to identify the branch.
        var product = await session.GetProductInfoAsync(
            appId,
            cancellationToken);

        if (product.AppId.Value != rawAppId)
        {
            throw new InvalidOperationException(
                "Steam returned product information for another app.");
        }

        var branches = product.Depots
            .Where(depot =>
                depot.DepotId.Value == installedDepot.DepotId)
            .SelectMany(depot => depot.Manifests)
            .Where(entry =>
                entry.Value.ManifestId.Value ==
                installedDepot.ManifestId)
            .Select(entry => entry.Key)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (branches.Length == 0)
        {
            throw new InvalidOperationException(
                $"Steam product metadata does not identify a branch " +
                $"for depot {installedDepot.DepotId}, " +
                $"manifest {installedDepot.ManifestId}. " +
                "The manifest may be older or restricted.");
        }

        if (branches.Length != 1 ||
            string.IsNullOrWhiteSpace(branches[0]))
        {
            throw new InvalidOperationException(
                "The installed manifest has an ambiguous " +
                "or invalid Steam branch.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var manifest = await session.GetManifestContents(
            appId,
            depotId,
            manifestId,
            branches[0],
            cancellationToken);

        // Never silently accept metadata for another depot or manifest.
        if (manifest.DepotId.Value != installedDepot.DepotId ||
            manifest.ManifestId.Value != installedDepot.ManifestId)
        {
            throw new InvalidOperationException(
                "The downloaded Steam manifest identifiers " +
                "do not match the requested installation metadata.");
        }

        if (manifest.Files is null)
        {
            throw new InvalidOperationException(
                "The downloaded Steam manifest has no file inventory.");
        }

        return manifest;
    }
}
