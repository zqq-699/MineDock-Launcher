using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class GameLanguageServiceTests : TestTempDirectory
{
    [Fact]
    public async Task ApplyLauncherLanguageCreatesOptionsFileForChinese()
    {
        var service = new GameLanguageService();
        var instance = CreateInstance();

        await service.ApplyLauncherLanguageAsync(instance, LauncherLanguages.SimplifiedChinese);

        var optionsPath = Path.Combine(instance.InstanceDirectory, "options.txt");
        Assert.True(File.Exists(optionsPath));
        Assert.Equal(["lang:zh_cn"], await File.ReadAllLinesAsync(optionsPath));
    }

    [Fact]
    public async Task ApplyLauncherLanguageCreatesOptionsFileForEnglish()
    {
        var service = new GameLanguageService();
        var instance = CreateInstance();

        await service.ApplyLauncherLanguageAsync(instance, LauncherLanguages.English);

        Assert.Equal(
            ["lang:en_us"],
            await File.ReadAllLinesAsync(Path.Combine(instance.InstanceDirectory, "options.txt")));
    }

    [Fact]
    public async Task ApplyLauncherLanguageCreatesOptionsFileForTraditionalChinese()
    {
        var service = new GameLanguageService();
        var instance = CreateInstance();

        await service.ApplyLauncherLanguageAsync(instance, LauncherLanguages.TraditionalChinese);

        Assert.Equal(
            ["lang:zh_tw"],
            await File.ReadAllLinesAsync(Path.Combine(instance.InstanceDirectory, "options.txt")));
    }

    [Fact]
    public async Task ApplyLauncherLanguageCreatesOptionsFileForJapanese()
    {
        var service = new GameLanguageService();
        var instance = CreateInstance();

        await service.ApplyLauncherLanguageAsync(instance, LauncherLanguages.Japanese);

        Assert.Equal(
            ["lang:ja_jp"],
            await File.ReadAllLinesAsync(Path.Combine(instance.InstanceDirectory, "options.txt")));
    }

    [Fact]
    public async Task ApplyLauncherLanguageReplacesExistingLanguageLine()
    {
        var service = new GameLanguageService();
        var instance = CreateInstance();
        Directory.CreateDirectory(instance.InstanceDirectory);
        var optionsPath = Path.Combine(instance.InstanceDirectory, "options.txt");
        await File.WriteAllLinesAsync(optionsPath, ["music:1.0", "lang:en_us", "fov:0.0"]);

        await service.ApplyLauncherLanguageAsync(instance, LauncherLanguages.SimplifiedChinese);

        Assert.Equal(["music:1.0", "lang:zh_cn", "fov:0.0"], await File.ReadAllLinesAsync(optionsPath));
    }

    [Fact]
    public async Task ApplyLauncherLanguageAppendsLanguageLineWhenMissing()
    {
        var service = new GameLanguageService();
        var instance = CreateInstance();
        Directory.CreateDirectory(instance.InstanceDirectory);
        var optionsPath = Path.Combine(instance.InstanceDirectory, "options.txt");
        await File.WriteAllLinesAsync(optionsPath, ["music:1.0", "fov:0.0"]);

        await service.ApplyLauncherLanguageAsync(instance, LauncherLanguages.English);

        Assert.Equal(["music:1.0", "fov:0.0", "lang:en_us"], await File.ReadAllLinesAsync(optionsPath));
    }

    [Fact]
    public async Task ApplyLauncherLanguageFallsBackToChineseForUnknownLauncherLanguage()
    {
        var service = new GameLanguageService();
        var instance = CreateInstance();

        await service.ApplyLauncherLanguageAsync(instance, "en-US");

        Assert.Equal(
            ["lang:zh_cn"],
            await File.ReadAllLinesAsync(Path.Combine(instance.InstanceDirectory, "options.txt")));
    }

    private GameInstance CreateInstance()
    {
        return new GameInstance
        {
            Id = "instance-1",
            Name = "Test Pack",
            VersionName = "Test Pack",
            InstanceDirectory = Path.Combine(TempRoot, "instances", Guid.NewGuid().ToString("N"))
        };
    }
}
