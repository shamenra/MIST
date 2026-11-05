using System.Reflection;

namespace NexusMods.App.UI.Controls.DevelopmentBuildBanner;

/// <summary>
/// M.I.S.T. product version and build identity from the compiled UI assembly.
/// A Git commit can be appended to AssemblyInformationalVersion after '+'.
/// It is build metadata, not part of the user-facing product version.
/// </summary>
internal static class MISTBuildInfo
{
    private static string InformationalVersion =>
        typeof(MISTBuildInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(MISTBuildInfo).Assembly.GetName().Version?.ToString()
        ?? "0.0.0-local";

    public static string Version => InformationalVersion.Split('+', 2)[0];

    public static string? Commit
    {
        get
        {
            var info = InformationalVersion;
            var plus = info.IndexOf('+');
            if (plus < 0 || plus == info.Length - 1) return null;

            // .NET SourceLink normally appends the full Git revision.
            // Other SemVer metadata must not be mislabeled as a Git build.
            var value = info[(plus + 1)..].Split('.')[0];
            if (value.Length is < 7 or > 40) return null;
            foreach (var character in value)
                if (!Uri.IsHexDigit(character)) return null;

            return value;
        }
    }

    public static string ShortBuild => Commit is { } sha ? sha[..Math.Min(8, sha.Length)] : "local";

    public static string Footer => $"v{Version}  ·  build {ShortBuild}";

    public static string Details => $"Version: v{Version}  |  Build: {Commit ?? "local (unversioned build)"}";
}
