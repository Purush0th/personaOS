using System.Globalization;
using System.Text.RegularExpressions;

namespace PersonaOS.Application.Updates;

/// <summary>
/// A release version as tagged on GitHub ("v0.1.0-alpha.9" or "1.2.0"), compared by Semantic
/// Versioning precedence: 1.0.0-alpha.2 &lt; 1.0.0-alpha.10 &lt; 1.0.0-beta &lt; 1.0.0. A plain
/// dotted compare read "0-alpha" as 0 and could not tell alpha.9 from alpha.10.
/// </summary>
public sealed partial record ReleaseVersion(int Major, int Minor, int Patch, IReadOnlyList<string> Prerelease)
    : IComparable<ReleaseVersion>
{
    [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")]
    private static partial Regex Pattern();

    public bool IsPrerelease => Prerelease.Count > 0;

    /// <summary>The version in a tag or an assembly's informational version, or null when it is not one.</summary>
    public static ReleaseVersion? Parse(string? text)
    {
        var match = Pattern().Match(text?.Trim() ?? string.Empty);
        if (!match.Success) return null;
        var pre = match.Groups["pre"].Success ? match.Groups["pre"].Value.Split('.') : [];
        return new ReleaseVersion(
            int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture),
            pre);
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null) return 1;
        var core = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (core != 0) return core;

        // A release outranks its own prereleases.
        if (!IsPrerelease || !other.IsPrerelease) return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;

        for (var i = 0; i < Math.Min(Prerelease.Count, other.Prerelease.Count); i++)
        {
            var a = Prerelease[i];
            var b = other.Prerelease[i];
            var aNumber = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var x);
            var bNumber = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var y);
            var step = (aNumber, bNumber) switch
            {
                (true, true) => x.CompareTo(y),
                (true, false) => -1, // Numbers sort before words.
                (false, true) => 1,
                _ => string.CompareOrdinal(a, b),
            };
            if (step != 0) return step;
        }
        return Prerelease.Count.CompareTo(other.Prerelease.Count);
    }

    public bool IsNewerThan(ReleaseVersion other) => CompareTo(other) > 0;

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPrerelease ? "-" + string.Join('.', Prerelease) : string.Empty);

    public bool Equals(ReleaseVersion? other) => other is not null && CompareTo(other) == 0;

    public override int GetHashCode() => ToString().GetHashCode(StringComparison.Ordinal);
}
