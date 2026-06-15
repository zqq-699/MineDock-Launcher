using Launcher.Domain.Models;
using Launcher.Infrastructure.FileSystem;

namespace Launcher.Tests.Infrastructure.Mods;

public sealed class ModServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ModServiceImportsDisablesAndEnablesJar()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "modded");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "example.jar");
        Directory.CreateDirectory(TempRoot);
        await File.WriteAllTextAsync(sourceJar, "fake jar");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = new ModService();

        var imported = await service.ImportAsync(instance, sourceJar);
        await service.SetEnabledAsync(imported, false);
        var disabled = (await service.GetModsAsync(instance)).Single();
        await service.SetEnabledAsync(disabled, true);
        var enabled = (await service.GetModsAsync(instance)).Single();

        Assert.True(enabled.IsEnabled);
        Assert.Equal("example.jar", enabled.FileName);
    }

}

