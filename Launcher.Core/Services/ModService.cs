using Launcher.Core.Models;

namespace Launcher.Core.Services;

public sealed class ModService : IModService
{
    public Task<IReadOnlyList<LocalMod>> GetModsAsync(GameInstance instance, CancellationToken cancellationToken = default)
    {
        var mods = new List<LocalMod>();
        var modsDirectory = GetModsDirectory(instance);
        var disabledDirectory = GetDisabledDirectory(instance);
        Directory.CreateDirectory(modsDirectory);
        Directory.CreateDirectory(disabledDirectory);

        foreach (var file in Directory.EnumerateFiles(modsDirectory, "*.jar"))
            mods.Add(ToLocalMod(file, true));

        foreach (var file in Directory.EnumerateFiles(disabledDirectory, "*.jar"))
            mods.Add(ToLocalMod(file, false));

        return Task.FromResult<IReadOnlyList<LocalMod>>(mods.OrderByDescending(m => m.IsEnabled).ThenBy(m => m.Name).ToList());
    }

    public async Task<LocalMod> ImportAsync(GameInstance instance, string sourceJarPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceJarPath))
            throw new FileNotFoundException("找不到要导入的 Mod 文件。", sourceJarPath);

        var modsDirectory = GetModsDirectory(instance);
        Directory.CreateDirectory(modsDirectory);

        var destination = Path.Combine(modsDirectory, Path.GetFileName(sourceJarPath));
        if (File.Exists(destination))
        {
            var name = Path.GetFileNameWithoutExtension(sourceJarPath);
            destination = Path.Combine(modsDirectory, $"{name}-{DateTimeOffset.Now:yyyyMMddHHmmss}.jar");
        }

        await using var source = File.OpenRead(sourceJarPath);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken);
        return ToLocalMod(destination, true);
    }

    public Task SetEnabledAsync(LocalMod mod, bool enabled, CancellationToken cancellationToken = default)
    {
        if (mod.IsEnabled == enabled)
            return Task.CompletedTask;

        var current = mod.FullPath;
        var instanceDirectory = enabled
            ? Directory.GetParent(Directory.GetParent(Path.GetDirectoryName(current)!)!.FullName)!.FullName
            : Directory.GetParent(Path.GetDirectoryName(current)!)!.FullName;

        var targetDirectory = enabled
            ? Path.Combine(instanceDirectory, "mods")
            : Path.Combine(instanceDirectory, ".launcher", "disabled-mods");

        Directory.CreateDirectory(targetDirectory);
        var targetPath = Path.Combine(targetDirectory, Path.GetFileName(current));
        File.Move(current, targetPath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(LocalMod mod, CancellationToken cancellationToken = default)
    {
        if (File.Exists(mod.FullPath))
            File.Delete(mod.FullPath);

        return Task.CompletedTask;
    }

    private static LocalMod ToLocalMod(string path, bool enabled)
    {
        var info = new FileInfo(path);
        return new LocalMod
        {
            Name = Path.GetFileNameWithoutExtension(path),
            FileName = Path.GetFileName(path),
            FullPath = path,
            IsEnabled = enabled,
            SizeBytes = info.Length,
            Source = "Local"
        };
    }

    private static string GetModsDirectory(GameInstance instance) => Path.Combine(instance.InstanceDirectory, "mods");
    private static string GetDisabledDirectory(GameInstance instance) => Path.Combine(instance.InstanceDirectory, ".launcher", "disabled-mods");
}
