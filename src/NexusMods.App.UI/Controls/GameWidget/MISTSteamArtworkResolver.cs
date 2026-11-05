using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace NexusMods.App.UI.Controls.GameWidget;

/// <summary>
/// M.I.S.T.-006-07A: Resolve artwork that Steam has already cached for a game.
/// Read only: no network calls, no copying, no modifications to Steam or game files.
/// A missing/invalid image is handled by GameWidgetViewModel's bundled-art fallback.
/// </summary>
internal static class MISTSteamArtworkResolver
{
    // Portrait art is preferred for the narrow My Games card; wide art is a last resort.
    private static readonly string[] CandidateNames =
    [
        "library_600x900.jpg", "library_600x900.png",
        "library_capsule.jpg", "library_capsule.png",
        "library_header.jpg", "library_header.png",
        "header.jpg", "header.png",
    ];

    internal static bool TryFindCoverArt(uint appId, out string path)
    {
        path = string.Empty;
        if (appId == 0) return false;

        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) return false;

            // Explicit paths, not XDG_DATA_HOME: a M.I.S.T. dev/test profile must
            // not hide the user's real Steam artwork. Flatpak is checked first.
            string[] caches =
            [
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "appcache", "librarycache"),
                Path.Combine(home, ".steam", "steam", "appcache", "librarycache"),
                Path.Combine(home, ".local", "share", "Steam", "appcache", "librarycache"),
            ];

            return TryFindCoverArtInCaches(appId, caches, out path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    // Separated to support fixture-based testing with temporary Steam cache roots.
    internal static bool TryFindCoverArtInCaches(uint appId, IEnumerable<string> caches, out string path)
    {
        path = string.Empty;
        if (appId == 0) return false;

        var appIdString = appId.ToString(CultureInfo.InvariantCulture);
        foreach (var assetName in CandidateNames)
        {
            foreach (var cache in caches)
            {
                try
                {
                    if (!Directory.Exists(cache)) continue;
                    var gameCache = Path.Combine(cache, appIdString);
                    // Steam's current format: librarycache/<appid>/<hash>/<asset>.
                    // Prefer the most recent cache entry when several hashes exist.
                    if (Directory.Exists(gameCache))
                    {
                        var hashDirectories = Directory.EnumerateDirectories(gameCache)
                            .Where(candidate => IsSteamHashDirectory(Path.GetFileName(candidate)))
                            .OrderByDescending(Directory.GetLastWriteTimeUtc)
                            .Take(128);
                        foreach (var hashDirectory in hashDirectories)
                        {
                            var candidate = Path.Combine(hashDirectory, assetName);
                            if (IsReadableCandidate(candidate))
                            {
                                path = candidate;
                                return true;
                            }
                        }

                        // Some Steam versions place artwork directly under <appid>.
                        var directPath = Path.Combine(gameCache, assetName);
                        if (IsReadableCandidate(directPath))
                        {
                            path = directPath;
                            return true;
                        }
                    }

                    // Steam legacy format: librarycache/<appid>_library_600x900.jpg.
                    var legacyPath = Path.Combine(cache, appIdString + "_" + assetName);
                    if (!IsReadableCandidate(legacyPath)) continue;
                    path = legacyPath;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // One inaccessible cache must not break game discovery.
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Locate Steam's small game icon (not the portrait library cover). Shared
    /// between all Steam games; no embedded art or network service required.
    /// </summary>
    internal static bool TryFindIconArt(uint appId, out string path)
    {
        path = string.Empty;
        if (appId == 0) return false;

        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) return false;

            // Deliberately independent of M.I.S.T.'s XDG_* test/dev profile.
            string[] caches =
            [
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "appcache", "librarycache"),
                Path.Combine(home, ".steam", "steam", "appcache", "librarycache"),
                Path.Combine(home, ".steam", "debian-installation", "appcache", "librarycache"),
                Path.Combine(home, ".local", "share", "Steam", "appcache", "librarycache"),
            ];
            return TryFindIconArtInCaches(appId, caches, out path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    // Separate cache-root parameter allows fixture-based tests without Steam.
    internal static bool TryFindIconArtInCaches(uint appId, IEnumerable<string> caches, out string path)
    {
        path = string.Empty;
        if (appId == 0) return false;

        var appIdString = appId.ToString(CultureInfo.InvariantCulture);
        string[] iconNames = ["icon.jpg", "icon.png", "icon.jpeg", "library_icon.jpg", "library_icon.png"];
        foreach (var cache in caches)
        {
            try
            {
                if (!Directory.Exists(cache)) continue;
                var appCache = Path.Combine(cache, appIdString);
                if (Directory.Exists(appCache))
                {
                    // Current Steam: librarycache/<appid>/<sha1>/icon.jpg
                    var hashed = Directory.EnumerateDirectories(appCache)
                        .Where(candidate => IsSteamHashDirectory(Path.GetFileName(candidate)))
                        .OrderByDescending(Directory.GetLastWriteTimeUtc)
                        .Take(128);
                    foreach (var hashDir in hashed)
                    foreach (var name in iconNames)
                    {
                        var candidate = Path.Combine(hashDir, name);
                        if (!IsReadableCandidate(candidate)) continue;
                        path = candidate;
                        return true;
                    }

                    foreach (var name in iconNames)
                    {
                        var candidate = Path.Combine(appCache, name);
                        if (!IsReadableCandidate(candidate)) continue;
                        path = candidate;
                        return true;
                    }
                }

                // Older Steam: librarycache/<appid>_icon.jpg
                foreach (var name in iconNames)
                {
                    var candidate = Path.Combine(cache, appIdString + "_" + name);
                    if (!IsReadableCandidate(candidate)) continue;
                    path = candidate;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // A broken/unreadable cache does not prevent normal game UI.
            }
        }

        return false;
    }

    /// <summary>
    /// Steam Library customization art is separate from the deprecated square
    /// icon cache. Look up its transparent game title logo and backdrop.
    /// </summary>
    internal static bool TryFindLogoArt(uint appId, out string path) =>
        TryFindSidebarAsset(appId, ["logo.png", "logo.jpg", "library_logo.png"], out path);

    internal static bool TryFindSidebarBackdrop(uint appId, out string path) =>
        TryFindSidebarAsset(appId,
            ["library_header.jpg", "library_header.png", "library_hero.jpg", "library_capsule.jpg", "library_600x900.jpg"], out path);

    private static bool TryFindSidebarAsset(uint appId, string[] names, out string path)
    {
        path = string.Empty;
        if (appId == 0) return false;
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home)) return false;
            string[] caches =
            [
                Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "appcache", "librarycache"),
                Path.Combine(home, ".steam", "steam", "appcache", "librarycache"),
                Path.Combine(home, ".steam", "debian-installation", "appcache", "librarycache"),
                Path.Combine(home, ".local", "share", "Steam", "appcache", "librarycache"),
            ];
            var appIdText = appId.ToString(CultureInfo.InvariantCulture);
            foreach (var name in names)
            foreach (var cache in caches)
            {
                try
                {
                    if (!Directory.Exists(cache)) continue;
                    var appDir = Path.Combine(cache, appIdText);
                    if (Directory.Exists(appDir))
                    {
                        foreach (var hashDir in Directory.EnumerateDirectories(appDir)
                                     .Where(candidate => IsSteamHashDirectory(Path.GetFileName(candidate)))
                                     .OrderByDescending(Directory.GetLastWriteTimeUtc)
                                     .Take(128))
                        {
                            var candidate = Path.Combine(hashDir, name);
                            if (!IsReadableCandidate(candidate)) continue;
                            path = candidate;
                            return true;
                        }
                        var direct = Path.Combine(appDir, name);
                        if (IsReadableCandidate(direct))
                        {
                            path = direct;
                            return true;
                        }
                    }
                    var legacy = Path.Combine(cache, appIdText + "_" + name);
                    if (!IsReadableCandidate(legacy)) continue;
                    path = legacy;
                    return true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // A damaged Steam cache must not prevent normal game UI.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Use the bundled icon when the user profile is inaccessible.
        }
        return false;
    }

    private static bool IsSteamHashDirectory(string name) =>
        name.Length == 40 && name.All(Uri.IsHexDigit);

    private static bool IsReadableCandidate(string path)
    {
        var info = new FileInfo(path);
        // Bound resource usage. Bitmap decoding is still protected by fallback.
        return info.Exists && info.Length > 0 && info.Length <= 20 * 1024 * 1024;
    }
}
