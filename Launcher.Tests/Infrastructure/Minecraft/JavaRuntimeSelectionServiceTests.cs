/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

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
    public async Task ManualSelectionThrowsWhenRuntimeVersionIsLowerThanRequirement()
    {
        var executablePath = @"C:\Java\jdk-8\bin\java.exe";
        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService
        {
            ImportedRuntime = CreateRuntime(executablePath, 8, "x64")
        });
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = executablePath
        };
        var instance = new GameInstance
        {
            MinecraftVersion = "1.20.5"
        };

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            service.SelectForLaunchAsync(instance, settings));

        Assert.Equal(JavaRuntimeSelectionFailureReason.ManualRuntimeVersionTooLow, exception.Reason);
        Assert.Equal(21, exception.RequiredMajorVersion);
        Assert.Equal(8, exception.CurrentMajorVersion);
    }

    [Fact]
    public async Task ManualSelectionAllowsLowerRuntimeWhenRequirementIsIgnored()
    {
        var executablePath = @"C:\Java\jdk-8\bin\java.exe";
        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService
        {
            ImportedRuntime = CreateRuntime(executablePath, 8, "x64")
        });
        var settings = new LauncherSettings
        {
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = executablePath
        };
        var instance = new GameInstance
        {
            MinecraftVersion = "1.20.5"
        };

        var selectedRuntime = await service.SelectForLaunchAsync(
            instance,
            settings,
            new LaunchRequestOptions(IgnoreJavaVersionRequirement: true));

        Assert.Equal(8, selectedRuntime.MajorVersion);
    }

    [Fact]
    public async Task AutomaticSelectionThrowsMissingReasonWhenNoRuntimeIsDiscovered()
    {
        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService());
        var settings = new LauncherSettings();
        var instance = new GameInstance
        {
            MinecraftVersion = "1.20.5"
        };

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            service.SelectForLaunchAsync(instance, settings));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeMissing, exception.Reason);
        Assert.Equal(21, exception.RequiredMajorVersion);
    }

    [Fact]
    public async Task AutomaticSelectionThrowsReasonWhenNoCompatibleRuntimeIsFound()
    {
        var service = new JavaRuntimeSelectionService(new FakeJavaRuntimeDiscoveryService
        {
            Runtimes =
            [
                CreateRuntime(@"C:\Java\jdk-17\bin\java.exe", 17, "x64")
            ]
        });
        var settings = new LauncherSettings();
        var instance = new GameInstance
        {
            MinecraftVersion = "1.20.5"
        };

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            service.SelectForLaunchAsync(instance, settings));

        Assert.Equal(JavaRuntimeSelectionFailureReason.AutomaticRuntimeNotFound, exception.Reason);
        Assert.Equal(21, exception.RequiredMajorVersion);
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

        var exception = await Assert.ThrowsAsync<JavaRuntimeSelectionException>(() =>
            service.SelectForLaunchAsync(new GameInstance(), settings));

        Assert.Equal(JavaRuntimeSelectionFailureReason.ManualRuntimeUnavailable, exception.Reason);
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
