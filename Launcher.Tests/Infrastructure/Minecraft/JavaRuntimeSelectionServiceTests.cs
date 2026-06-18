using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Infrastructure.Minecraft;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Minecraft;

public sealed class JavaRuntimeSelectionServiceTests : TestTempDirectory
{
    [Fact]
    public async Task AutomaticSelectionUsesVersionJsonJavaMajorVersion()
    {
        var minecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", "1.20.1");
        Directory.CreateDirectory(versionDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(versionDirectory, "1.20.1.json"),
            """
            {
              "id": "1.20.1",
              "javaVersion": {
                "component": "java-runtime-gamma",
                "majorVersion": 17
              }
            }
            """);

        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateRuntime(@"C:\Java\jdk-21\bin\java.exe", 21, "x64"),
                CreateRuntime(@"C:\Java\jdk-17\bin\java.exe", 17, "x64")
            ]
        });
        var settings = new LauncherSettings
        {
            MinecraftDirectory = minecraftDirectory,
            JavaSelectionMode = JavaSelectionMode.Auto
        };
        var instance = new GameInstance
        {
            MinecraftVersion = "1.20.1",
            VersionName = "1.20.1"
        };

        var selectedRuntime = await service.SelectForLaunchAsync(instance, settings);

        Assert.Equal(17, selectedRuntime.MajorVersion);
    }

    [Theory]
    [InlineData("1.20.5", 21)]
    [InlineData("1.21.4", 21)]
    [InlineData("1.18", 17)]
    [InlineData("1.20.4", 17)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.16.5", 8)]
    public void GuessRequiredMajorVersionMapsMinecraftVersions(string minecraftVersion, int expectedMajorVersion)
    {
        Assert.Equal(expectedMajorVersion, JavaRuntimeSelectionService.GuessRequiredMajorVersion(minecraftVersion));
    }

    [Fact]
    public void AutomaticSelectionPrefersExactVersionAndX64()
    {
        var selectedRuntime = JavaRuntimeSelectionService.SelectBestRuntime(
            [
                CreateRuntime(@"C:\Java\jdk-17-x86\bin\java.exe", 17, "x86"),
                CreateRuntime(@"C:\Java\jdk-17\bin\java.exe", 17, "x64"),
                CreateRuntime(@"C:\Java\jdk-21\bin\java.exe", 21, "x64")
            ],
            17);

        Assert.Equal(@"C:\Java\jdk-17\bin\java.exe", selectedRuntime?.ExecutablePath);
    }

    [Fact]
    public void AutomaticSelectionFallsBackToSmallestHigherVersion()
    {
        var selectedRuntime = JavaRuntimeSelectionService.SelectBestRuntime(
            [
                CreateRuntime(@"C:\Java\jdk-21\bin\java.exe", 21, "x64"),
                CreateRuntime(@"C:\Java\jdk-22\bin\java.exe", 22, "x64"),
                CreateRuntime(@"C:\Java\jdk-8\bin\java.exe", 8, "x64")
            ],
            17);

        Assert.Equal(21, selectedRuntime?.MajorVersion);
    }

    [Fact]
    public void AutomaticSelectionDoesNotChooseLowerVersionThanRequired()
    {
        var selectedRuntime = JavaRuntimeSelectionService.SelectBestRuntime(
            [
                CreateRuntime(@"C:\Java\jdk-21\bin\java.exe", 21, "x64"),
                CreateRuntime(@"C:\Java\jdk-17\bin\java.exe", 17, "x64")
            ],
            25);

        Assert.Null(selectedRuntime);
    }

    [Fact]
    public async Task ManualSelectionUsesSavedExecutablePath()
    {
        var executablePath = @"C:\Java\jdk-21\bin\java.exe";
        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService
        {
            ImportedRuntime = CreateRuntime(executablePath, 21, "x64")
        });
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = executablePath
        };

        var selectedRuntime = await service.SelectForLaunchAsync(new GameInstance(), settings);

        Assert.Equal(executablePath, selectedRuntime.ExecutablePath);
    }

    [Fact]
    public async Task ManualSelectionThrowsWhenPathIsMissing()
    {
        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService());
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual
        };

        await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            service.SelectForLaunchAsync(new GameInstance(), settings));
    }

    [Fact]
    public async Task ManualSelectionThrowsWhenPathCannotBeProbed()
    {
        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService
        {
            ImportExceptionToThrow = new FileNotFoundException("missing")
        });
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = @"C:\missing\java.exe"
        };

        await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            service.SelectForLaunchAsync(new GameInstance(), settings));
    }

    private static JavaRuntimeInfo CreateRuntime(string executablePath, int majorVersion, string architecture)
    {
        var installationDirectory = Path.GetDirectoryName(Path.GetDirectoryName(executablePath)) ?? string.Empty;
        return new JavaRuntimeInfo(
            $"Java {majorVersion}",
            $"{majorVersion}.0.0",
            majorVersion,
            architecture,
            executablePath,
            installationDirectory,
            "Test");
    }

    private sealed class FakeJavaRuntimeDiscoveryService : IJavaRuntimeDiscoveryService
    {
        public IReadOnlyList<JavaRuntimeInfo> Runtimes { get; init; } = [];

        public JavaRuntimeInfo ImportedRuntime { get; init; } = CreateRuntime(
            @"C:\Java\jdk-21\bin\java.exe",
            21,
            "x64");

        public Exception? ImportExceptionToThrow { get; init; }

        public Task<IReadOnlyList<JavaRuntimeInfo>> DiscoverAsync(
            string? minecraftDirectory,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Runtimes);
        }

        public Task<JavaRuntimeInfo> DiscoverExecutableAsync(
            string executablePath,
            CancellationToken cancellationToken = default)
        {
            if (ImportExceptionToThrow is not null)
                throw ImportExceptionToThrow;

            return Task.FromResult(ImportedRuntime);
        }
    }
}
