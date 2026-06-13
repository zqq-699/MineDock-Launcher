using CmlLib.Core;
using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class GameVersionService : IGameVersionService
{
    public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(CancellationToken cancellationToken = default)
    {
        var launcher = new MinecraftLauncher();
        var versions = await launcher.GetAllVersionsAsync(cancellationToken);
        return versions
            .Select(v => new MinecraftVersionInfo(v.Name, v.Type?.ToString() ?? string.Empty, false, v.ReleaseTime))
            .OrderBy(v => VersionTypeRank(v.Type))
            .ThenByDescending(v => v.Type.Equals("Release", StringComparison.OrdinalIgnoreCase) ? VersionSortKey(v.Name) : new Version(0, 0))
            .ThenByDescending(v => v.ReleaseTime ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static int VersionTypeRank(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "release" => 0,
            "snapshot" => 1,
            "old_beta" => 2,
            "old_alpha" => 3,
            _ => 4
        };
    }

    private static Version VersionSortKey(string name)
    {
        var clean = name.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0);
    }
}
