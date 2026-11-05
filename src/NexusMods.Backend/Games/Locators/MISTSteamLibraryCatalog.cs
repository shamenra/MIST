using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusMods.Backend.Games.Locators;

/// <summary>
/// A known Steam library. Offline entries are retained.
/// Online means steamapps is accessible, NOT that a removable
/// drive's identity has been authenticated.
/// </summary>
public sealed record MISTSteamLibraryStatus(
    string Path,
    bool UserAdded,
    bool IsOnline,
    int ManifestCount,
    DateTimeOffset? LastSeenOnlineUtc);

/// <summary>
/// Persistent, non-recursive Steam library discovery.
///
/// Reads native and Flatpak Steam registries, plus locations
/// explicitly registered by the user.
///
/// Never removes a library merely because it is offline.
/// Never writes inside a Steam library.
/// </summary>
public sealed class MISTSteamLibraryCatalog
{
    private sealed record StoredLibrary(
        string Path,
        bool UserAdded,
        DateTimeOffset? LastSeenOnlineUtc);

    private readonly string _homeDirectory;
    private readonly string _stateFile;

    private readonly Dictionary<string, StoredLibrary> _stored =
        new(StringComparer.Ordinal);

    private static readonly Regex LibraryPathPattern = new(
        "\"path\"\\s+\"(?<path>[^\"]+)\"",
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant |
        RegexOptions.Compiled);

    public MISTSteamLibraryCatalog(
        string homeDirectory,
        string stateFile)
    {
        _homeDirectory = Normalize(homeDirectory);
        _stateFile = System.IO.Path.GetFullPath(stateFile);

        if (!File.Exists(_stateFile))
            return;

        var contents = File.ReadAllText(_stateFile);

        var entries =
            JsonSerializer.Deserialize<StoredLibrary[]>(contents)
            ?? throw new InvalidDataException(
                "M.I.S.T. Steam library catalog is invalid.");

        foreach (var entry in entries)
        {
            var path = Normalize(entry.Path);

            if (!_stored.TryAdd(
                    path,
                    entry with { Path = path }))
            {
                throw new InvalidDataException(
                    "Duplicate Steam library catalog entry: " +
                    path);
            }
        }
    }

    public static MISTSteamLibraryCatalog CreateForCurrentUser()
    {
        var home = Environment.GetEnvironmentVariable("HOME");

        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile);
        }

        var data = Environment.GetEnvironmentVariable(
            "XDG_DATA_HOME");

        if (string.IsNullOrWhiteSpace(data))
        {
            data = System.IO.Path.Combine(
                home, ".local", "share");
        }

        var stateFile = System.IO.Path.Combine(
            data,
            "ModIntegrationAndSortingTechnology",
            "SteamLibraries",
            "catalog.json");

        return new MISTSteamLibraryCatalog(home, stateFile);
    }

    /// <summary>
    /// Explicitly registers a library path.
    ///
    /// An absent path is permitted: it may be an unplugged
    /// external drive. An existing non-library directory
    /// is rejected.
    /// </summary>
    public void AddManual(string path)
    {
        path = Normalize(path);

        if (Directory.Exists(path) &&
            !Directory.Exists(
                System.IO.Path.Combine(path, "steamapps")))
        {
            throw new InvalidDataException(
                "The selected directory exists but does not " +
                "contain a Steam steamapps directory.");
        }

        if (_stored.TryGetValue(path, out var existing))
        {
            if (existing.UserAdded)
                return;

            _stored[path] = existing with
            {
                UserAdded = true
            };
        }
        else
        {
            _stored[path] = new StoredLibrary(
                path,
                true,
                null);
        }

        Save();
    }

    /// <summary>
    /// Refreshes known libraries without recursively scanning
    /// drives or deleting unavailable registrations.
    /// </summary>
    public IReadOnlyList<MISTSteamLibraryStatus> Refresh()
    {
        var discovered = new HashSet<string>(
            StringComparer.Ordinal);

        var roots = GetKnownSteamRoots();

        foreach (var root in roots)
        {
            var steamapps = System.IO.Path.Combine(
                root, "steamapps");

            if (!Directory.Exists(steamapps))
                continue;

            discovered.Add(Normalize(root));

            var registry = System.IO.Path.Combine(
                steamapps, "libraryfolders.vdf");

            if (!File.Exists(registry))
                continue;

            string contents;

            try
            {
                contents = File.ReadAllText(registry);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in
                     LibraryPathPattern.Matches(contents))
            {
                var raw = match.Groups["path"].Value;

                if (!System.IO.Path.IsPathFullyQualified(raw))
                    continue;

                try
                {
                    discovered.Add(Normalize(raw));
                }
                catch (ArgumentException)
                {
                    // Ignore an invalid registry entry.
                }
            }
        }

        var changed = false;

        foreach (var path in discovered)
        {
            if (_stored.ContainsKey(path))
                continue;

            _stored.Add(
                path,
                new StoredLibrary(path, false, null));

            changed = true;
        }

        var now = DateTimeOffset.UtcNow;
        var statuses = new List<MISTSteamLibraryStatus>();

        foreach (var (path, entry) in _stored.ToArray())
        {
            var steamapps = System.IO.Path.Combine(
                path, "steamapps");

            var online = Directory.Exists(steamapps);
            var manifestCount = 0;

            if (online)
            {
                try
                {
                    manifestCount = Directory.EnumerateFiles(
                        steamapps,
                        "appmanifest_*.acf",
                        SearchOption.TopDirectoryOnly).Count();
                }
                catch (IOException)
                {
                    online = false;
                }
                catch (UnauthorizedAccessException)
                {
                    online = false;
                }
            }

            var lastSeen = entry.LastSeenOnlineUtc;

            if (online &&
                (lastSeen is null ||
                 now - lastSeen.Value > TimeSpan.FromHours(24)))
            {
                lastSeen = now;

                _stored[path] = entry with
                {
                    LastSeenOnlineUtc = lastSeen
                };

                changed = true;
            }

            statuses.Add(new MISTSteamLibraryStatus(
                path,
                entry.UserAdded,
                online,
                online ? manifestCount : 0,
                lastSeen));
        }

        if (changed)
            Save();

        return statuses
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToArray();
    }

    private IEnumerable<string> GetKnownSteamRoots()
    {
        yield return System.IO.Path.Combine(
            _homeDirectory, ".steam", "steam");

        yield return System.IO.Path.Combine(
            _homeDirectory, ".steam", "debian-installation");

        yield return System.IO.Path.Combine(
            _homeDirectory, ".local", "share", "Steam");

        yield return System.IO.Path.Combine(
            _homeDirectory, ".var", "app",
            "com.valvesoftware.Steam",
            ".local", "share", "Steam");

        yield return System.IO.Path.Combine(
            _homeDirectory, ".var", "app",
            "com.valvesoftware.Steam",
            "data", "Steam");
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !System.IO.Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Steam library path must be absolute.",
                nameof(path));
        }

        var normalized =
            System.IO.Path.GetFullPath(path);

        if (normalized !=
            System.IO.Path.GetPathRoot(normalized))
        {
            normalized = normalized.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar);
        }

        return normalized;
    }

    private void Save()
    {
        var directory = System.IO.Path.GetDirectoryName(
            _stateFile)!;

        Directory.CreateDirectory(directory);

        var temporary = _stateFile + "." +
                        Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(
                    stream,
                    _stored.Values
                        .OrderBy(x => x.Path, StringComparer.Ordinal)
                        .ToArray(),
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                stream.Flush(flushToDisk: true);
            }

            File.Move(
                temporary,
                _stateFile,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
