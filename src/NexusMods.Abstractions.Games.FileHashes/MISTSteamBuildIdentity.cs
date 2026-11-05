using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Local Steam installation identity. The Steam build ID is NOT a public game
/// version, and this value alone does not establish mod compatibility.
/// </summary>
public sealed record MISTSteamBuildIdentity(
    uint AppId,
    ulong BuildId,
    string AppManifestPath,
    IReadOnlyDictionary<uint, ulong> InstalledDepots)
{
    // Content identity is independent of the installation path; consumers can
    // use (normalized installation path, Signature) to track separate installs.
    public string Signature
    {
        get
        {
            var canonical = $"{AppId}|{BuildId}|" + string.Join(";",
                InstalledDepots.OrderBy(item => item.Key)
                    .Select(item => $"{item.Key}:{item.Value}"));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
        }
    }
}

/// <summary>
/// Reads Steam metadata next to a specific installation. Never writes game,
/// Steam, database, or application files. Does not guess the game's version.
/// </summary>
public static class MISTSteamBuildIdentityReader
{
    public static bool TryReadForGameFolder(string gameFolder, out MISTSteamBuildIdentity? result)
    {
        result = null;
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(gameFolder));
            // Steam's normal layout: [library]/steamapps/common/[install dir].
            var common = directory.Parent;
            var steamapps = common?.Parent;
            if (common is null || steamapps is null ||
                !common.Name.Equals("common", StringComparison.OrdinalIgnoreCase) ||
                !steamapps.Name.Equals("steamapps", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Directory.Exists(directory.FullName)) return false;
            MISTSteamBuildIdentity? match = null;
            foreach (var path in Directory.EnumerateFiles(steamapps.FullName, "appmanifest_*.acf"))
            {
                if (!TryReadManifest(path, directory.Name, out var identity)) continue;
                // More than one manifest claiming this directory is ambiguous.
                if (match is not null) return false;
                match = identity;
            }

            result = match;
            return result is not null;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public static bool TryReadManifest(string manifestPath, string expectedInstallDirectory,
        out MISTSteamBuildIdentity? result)
    {
        result = null;
        try
        {
            var info = new FileInfo(manifestPath);
            if (!info.Exists || info.Length > 8 * 1024 * 1024) return false;
            var raw = File.ReadAllText(manifestPath);
            var tokens = Tokenize(raw);
            var index = 0;
            if (tokens.Count < 3 || !tokens[index++].Equals("AppState", StringComparison.OrdinalIgnoreCase) ||
                tokens[index++] != "{") return false;
            var root = ReadObject(tokens, ref index);
            if (index != tokens.Count ||
                !GetString(root, "installdir", out var installDir) ||
                !installDir.Equals(expectedInstallDirectory, StringComparison.OrdinalIgnoreCase) ||
                !GetString(root, "appid", out var rawAppId) ||
                !uint.TryParse(rawAppId, out var appId) ||
                !GetString(root, "buildid", out var rawBuildId) ||
                !ulong.TryParse(rawBuildId, out var buildId) ||
                !root.TryGetValue("InstalledDepots", out var depotsValue) ||
                depotsValue is not Dictionary<string, object> depotMap)
                return false;

            var filename = Path.GetFileName(manifestPath);
            if (!filename.Equals($"appmanifest_{appId}.acf", StringComparison.OrdinalIgnoreCase))
                return false;

            var depots = new SortedDictionary<uint, ulong>();
            foreach (var (depotIdRaw, details) in depotMap)
            {
                if (!uint.TryParse(depotIdRaw, out var depotId) ||
                    details is not Dictionary<string, object> detailMap ||
                    !GetString(detailMap, "manifest", out var rawManifest) ||
                    !ulong.TryParse(rawManifest, out var manifestId))
                    return false;
                depots.Add(depotId, manifestId);
            }
            if (depots.Count == 0) return false;
            result = new MISTSteamBuildIdentity(appId, buildId,
                Path.GetFullPath(manifestPath), depots);
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
        catch (InvalidDataException) { return false; }
    }

    private static bool GetString(Dictionary<string, object> map, string key, out string value)
    {
        value = "";
        if (!map.TryGetValue(key, out var item) || item is not string text) return false;
        value = text;
        return true;
    }

    private static Dictionary<string, object> ReadObject(IReadOnlyList<string> tokens, ref int position)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        while (position < tokens.Count)
        {
            var key = tokens[position++];
            if (key == "}") return result;
            if (key is "{" or "}" || position == tokens.Count)
                throw new InvalidDataException("Malformed Steam appmanifest object");
            var value = tokens[position++];
            object node = value == "{" ? ReadObject(tokens, ref position) : value;
            if (value == "}") throw new InvalidDataException("Unexpected closing brace");
            if (!result.TryAdd(key, node))
                throw new InvalidDataException("Duplicate Steam appmanifest key");
        }
        throw new InvalidDataException("Unterminated Steam appmanifest object");
    }

    private static List<string> Tokenize(string source)
    {
        var tokens = new List<string>();
        for (var i = 0; i < source.Length;)
        {
            if (char.IsWhiteSpace(source[i])) { i++; continue; }
            if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                i += 2;
                while (i < source.Length && source[i] != '\n') i++;
                continue;
            }
            if (source[i] is '{' or '}')
            {
                tokens.Add(source[i++].ToString());
                continue;
            }
            if (source[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                var closed = false;
                while (i < source.Length)
                {
                    var c = source[i++];
                    if (c == '"') { closed = true; break; }
                    if (c == '\\' && i < source.Length && source[i] is '"' or '\\')
                        c = source[i++];
                    sb.Append(c);
                }
                if (!closed) throw new InvalidDataException("Unterminated string");
                tokens.Add(sb.ToString());
                continue;
            }
            var start = i;
            while (i < source.Length && !char.IsWhiteSpace(source[i]) &&
                   source[i] is not ('{' or '}' or '"')) i++;
            if (i == start) throw new InvalidDataException("Invalid token");
            tokens.Add(source[start..i]);
        }
        return tokens;
    }
}
