namespace Heimdall.Core.Updates;

/// <summary>
/// Decides whether a GitHub release tag represents a newer version than the one currently running.
/// Pure and platform-agnostic: the caller supplies the running <see cref="Version"/>.
/// </summary>
public static class UpdateCheck
{
    /// <summary>
    /// Returns true when <paramref name="latestTag"/> parses to a strictly higher version than
    /// <paramref name="current"/>. Tags may carry a leading <c>v</c> and a SemVer
    /// <c>-prerelease</c>/<c>+metadata</c> suffix, both of which are ignored. Comparison is on
    /// major.minor.patch only; an unparseable tag yields false (treated as "no update").
    /// </summary>
    public static bool IsUpdateAvailable(Version current, string latestTag)
    {
        if (!TryParseTag(latestTag, out var latest))
            return false;

        return Normalise(latest) > Normalise(current);
    }

    private static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var text = tag.Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
            text = text[1..];

        // Drop a SemVer prerelease (-rc.1) or build-metadata (+sha) suffix.
        var suffix = text.IndexOfAny(['-', '+']);
        if (suffix >= 0)
            text = text[..suffix];

        // System.Version needs at least major.minor; pad a single component so "2" -> "2.0".
        if (!text.Contains('.'))
            text += ".0";

        return Version.TryParse(text, out version!);
    }

    // System.Version treats unspecified components as -1, so a 3-part tag would sort below an
    // otherwise-equal 4-part assembly version. Compare only major.minor.patch, with patch floored at 0.
    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
