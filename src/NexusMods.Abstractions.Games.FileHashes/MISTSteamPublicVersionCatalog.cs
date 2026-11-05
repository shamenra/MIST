using System;
using System.Collections.Generic;
using System.Linq;

namespace NexusMods.Abstractions.Games.FileHashes;

/// <summary>
/// Public game-version aliases backed by specifically identified Steam depot
/// manifests. A manifest identifies a Steam content snapshot; it does NOT
/// constitute proof that installed files are unchanged or grant deletion rights.
///
/// These initial curated aliases are a bootstrap. Future automatic version
/// metadata acquisition should add evidence-backed mappings without replacing
/// mappings for historical builds. Never infer a public version from build ID.
/// </summary>
public static class MISTSteamPublicVersionCatalog
{
    private sealed record Alias(uint AppId, string PublicVersion, ulong[] ManifestIds, string Evidence);

    private static readonly Alias[] Known =
    [
        // Cyberpunk: verified NexusHashFix alias for the installed Steam depot.
        new Alias(1091500, "2.5", [3524634124112875626UL],
            "NexusHashFix: version_aliases.yaml, manifest 3524634124112875626"),

        // Oblivion: SteamDB build 19115871 is Update 1.2; Bethesda's
        // official July 9 2025 announcement identifies the public version.
        new Alias(2623190, "1.2", [7537075278498631900UL],
            "SteamDB patchnotes/19115871 and Bethesda Update 1.2"),
    ];

    /// <summary>
    /// Resolve ONLY when the entire installed depot-manifest set matches a
    /// single curated mapping. Unknown, partial and ambiguous sets fail closed.
    /// The existing IFileHashesService remains the authoritative caller.
    /// </summary>
    public static bool TryResolve(IEnumerable<string> rawManifestIds, out string? publicVersion)
    {
        publicVersion = null;
        if (rawManifestIds is null) return false;

        var parsed = new HashSet<ulong>();
        foreach (var raw in rawManifestIds)
        {
            if (!ulong.TryParse(raw, out var id) || id == 0 || !parsed.Add(id))
                return false;
        }
        if (parsed.Count == 0) return false;

        var matches = Known.Where(alias => parsed.SetEquals(alias.ManifestIds))
            .Take(2).ToArray();
        if (matches.Length != 1) return false;

        publicVersion = matches[0].PublicVersion;
        return true;
    }
}
