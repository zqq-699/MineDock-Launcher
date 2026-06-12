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
            .Select(v => new MinecraftVersionInfo(v.Name, v.Type.ToString(), false))
            .OrderByDescending(v => VersionSortKey(v.Name))
            .ThenBy(v => v.Name)
            .ToList();
    }

    private static Version VersionSortKey(string name)
    {
        var clean = name.Split('-', StringSplitOptions.RemoveEmptyEntries)[0];
        return Version.TryParse(clean, out var version) ? version : new Version(0, 0);
    }
}
