using Launcher.App.Resources;

namespace Launcher.App.ViewModels.Resources;

public static class ResourceMinecraftVersionSupportFormatter
{
    public static string Format(
        IReadOnlyList<string> supportedVersions,
        IReadOnlyList<string>? releaseVersionOrder = null)
    {
        var normalizedVersions = NormalizeVersions(supportedVersions);
        if (normalizedVersions.Count == 0)
            return Strings.Resources_ModVersionsUnknown;

        var releaseOrder = NormalizeVersions(releaseVersionOrder ?? []);
        if (releaseOrder.Count == 0)
            return string.Join(", ", normalizedVersions.Select(version => version.DisplayText));

        var releaseRanks = releaseOrder
            .Select((version, index) => (version.Key, Index: index))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var sortedVersions = normalizedVersions
            .Select((version, index) => new RankedVersion(
                version,
                releaseRanks.TryGetValue(version.Key, out var rank) ? rank : null,
                index))
            .OrderBy(version => version.Rank ?? int.MaxValue)
            .ThenBy(version => version.OriginalIndex)
            .ToList();

        return string.Join(", ", CreateSegments(sortedVersions));
    }

    private static IReadOnlyList<string> CreateSegments(IReadOnlyList<RankedVersion> versions)
    {
        var segments = new List<string>();
        var currentSegment = new List<RankedVersion>();

        foreach (var version in versions)
        {
            if (currentSegment.Count == 0 || IsConsecutive(currentSegment[^1], version))
            {
                currentSegment.Add(version);
                continue;
            }

            segments.Add(FormatSegment(currentSegment));
            currentSegment.Clear();
            currentSegment.Add(version);
        }

        if (currentSegment.Count > 0)
            segments.Add(FormatSegment(currentSegment));

        return segments;
    }

    private static bool IsConsecutive(RankedVersion previous, RankedVersion current)
    {
        return previous.Rank.HasValue
            && current.Rank.HasValue
            && current.Rank.Value - previous.Rank.Value == 1;
    }

    private static string FormatSegment(IReadOnlyList<RankedVersion> segment)
    {
        return segment.Count > 1
            ? $"{segment[^1].Version.DisplayText}+"
            : segment[0].Version.DisplayText;
    }

    private static IReadOnlyList<NormalizedMinecraftVersion> NormalizeVersions(IEnumerable<string> versions)
    {
        return versions
            .Select(TryNormalizeMinecraftVersion)
            .Where(version => version is not null)
            .Select(version => version!.Value)
            .GroupBy(version => version.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static NormalizedMinecraftVersion? TryNormalizeMinecraftVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return null;

        var trimmed = version.Trim();
        if (int.TryParse(trimmed, out var singlePartVersion))
            return new NormalizedMinecraftVersion(trimmed, singlePartVersion.ToString());

        var parts = trimmed.Split('.');
        if (parts.Length < 2
            || !TryParseLeadingNumber(parts[0], out var major)
            || !TryParseLeadingNumber(parts[1], out var minor))
        {
            return null;
        }

        var displayText = $"{major}.{minor}";
        return new NormalizedMinecraftVersion(displayText, displayText);
    }

    private static bool TryParseLeadingNumber(string value, out int number)
    {
        number = 0;
        var end = 0;
        while (end < value.Length && char.IsDigit(value[end]))
            end++;

        return end > 0 && int.TryParse(value[..end], out number);
    }

    private readonly record struct NormalizedMinecraftVersion(string Key, string DisplayText);

    private readonly record struct RankedVersion(
        NormalizedMinecraftVersion Version,
        int? Rank,
        int OriginalIndex);
}
