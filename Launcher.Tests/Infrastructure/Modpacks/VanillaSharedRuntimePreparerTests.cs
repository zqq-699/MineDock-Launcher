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

using Launcher.Domain.Models;
using Launcher.Infrastructure.Modpacks;
using Launcher.Tests.Helpers;

namespace Launcher.Tests.Infrastructure.Modpacks;

public sealed class VanillaSharedRuntimePreparerTests : TestTempDirectory
{
    [Fact]
    public async Task PrepareAsyncCopiesSharedRuntimeWithoutCreatingRealVersionDirectory()
    {
        var targetMinecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var preparer = new VanillaSharedRuntimePreparer(
            new ScriptedVanillaVersionInstaller((_, sandboxMinecraftDirectory) =>
            {
                CreateSandboxVersion(sandboxMinecraftDirectory, "1.20.1");
                CreateSandboxSharedRuntime(sandboxMinecraftDirectory);
                File.WriteAllText(Path.Combine(sandboxMinecraftDirectory, "launcher_profiles.json"), "{}");
                return Task.CompletedTask;
            }),
            TempRoot);

        await preparer.PrepareAsync("1.20.1", targetMinecraftDirectory, progress: null);

        Assert.False(Directory.Exists(Path.Combine(targetMinecraftDirectory, "versions", "1.20.1")));
        Assert.True(File.Exists(Path.Combine(targetMinecraftDirectory, "libraries", "com", "example", "demo", "1.0.0", "demo-1.0.0.jar")));
        Assert.True(File.Exists(Path.Combine(targetMinecraftDirectory, "assets", "indexes", "1.20.json")));
        Assert.True(File.Exists(Path.Combine(targetMinecraftDirectory, "assets", "objects", "ab", "abcdef0123456789")));
        Assert.True(File.Exists(Path.Combine(targetMinecraftDirectory, "assets", "log_configs", "client.xml")));
        Assert.False(File.Exists(Path.Combine(targetMinecraftDirectory, "launcher_profiles.json")));
    }

    [Fact]
    public async Task PrepareAsyncDoesNotOverwriteExistingSharedFiles()
    {
        var targetMinecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var existingLibraryPath = Path.Combine(
            targetMinecraftDirectory,
            "libraries",
            "com",
            "example",
            "demo",
            "1.0.0",
            "demo-1.0.0.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(existingLibraryPath)!);
        await File.WriteAllTextAsync(existingLibraryPath, "existing");

        var preparer = new VanillaSharedRuntimePreparer(
            new ScriptedVanillaVersionInstaller((_, sandboxMinecraftDirectory) =>
            {
                CreateSandboxVersion(sandboxMinecraftDirectory, "1.20.1");
                CreateSandboxSharedRuntime(sandboxMinecraftDirectory, libraryContent: "sandbox");
                return Task.CompletedTask;
            }),
            TempRoot);

        await preparer.PrepareAsync("1.20.1", targetMinecraftDirectory, progress: null);

        Assert.Equal("existing", await File.ReadAllTextAsync(existingLibraryPath));
    }

    [Fact]
    public async Task PrepareAsyncCleansSandboxWhenInstallerFailsWithoutTouchingRealVersionsDirectory()
    {
        var sandboxRoot = Path.Combine(TempRoot, "sandbox-root");
        var targetMinecraftDirectory = Path.Combine(TempRoot, ".minecraft");
        var preparer = new VanillaSharedRuntimePreparer(
            new ScriptedVanillaVersionInstaller((_, sandboxMinecraftDirectory) =>
            {
                CreateSandboxVersion(sandboxMinecraftDirectory, "1.20.1");
                throw new InvalidOperationException("sandbox install failed");
            }),
            sandboxRoot);

        await Assert.ThrowsAsync<InvalidOperationException>(() => preparer.PrepareAsync(
            "1.20.1",
            targetMinecraftDirectory,
            progress: null,
            downloadSourcePreference: DownloadSourcePreference.Auto));

        Assert.False(Directory.Exists(Path.Combine(targetMinecraftDirectory, "versions", "1.20.1")));
        var runtimeSandboxDirectory = Path.Combine(sandboxRoot, "launcher-vanilla-runtime");
        Assert.False(Directory.Exists(runtimeSandboxDirectory)
            && Directory.GetDirectories(runtimeSandboxDirectory).Length > 0);
    }

    private static void CreateSandboxVersion(string minecraftDirectory, string versionName)
    {
        var versionDirectory = Path.Combine(minecraftDirectory, "versions", versionName);
        Directory.CreateDirectory(versionDirectory);
        File.WriteAllText(Path.Combine(versionDirectory, $"{versionName}.json"), "{}");
        File.WriteAllText(Path.Combine(versionDirectory, $"{versionName}.jar"), "sandbox jar");
    }

    private static void CreateSandboxSharedRuntime(string minecraftDirectory, string libraryContent = "sandbox library")
    {
        var libraryPath = Path.Combine(
            minecraftDirectory,
            "libraries",
            "com",
            "example",
            "demo",
            "1.0.0",
            "demo-1.0.0.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.WriteAllText(libraryPath, libraryContent);

        var assetIndexPath = Path.Combine(minecraftDirectory, "assets", "indexes", "1.20.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assetIndexPath)!);
        File.WriteAllText(assetIndexPath, "{}");

        var assetObjectPath = Path.Combine(minecraftDirectory, "assets", "objects", "ab", "abcdef0123456789");
        Directory.CreateDirectory(Path.GetDirectoryName(assetObjectPath)!);
        File.WriteAllText(assetObjectPath, "asset");

        var logConfigPath = Path.Combine(minecraftDirectory, "assets", "log_configs", "client.xml");
        Directory.CreateDirectory(Path.GetDirectoryName(logConfigPath)!);
        File.WriteAllText(logConfigPath, "<xml />");
    }

    private sealed class ScriptedVanillaVersionInstaller : IVanillaVersionInstaller
    {
        private readonly Func<string, string, Task> callback;

        public ScriptedVanillaVersionInstaller(Func<string, string, Task> callback)
        {
            this.callback = callback;
        }

        public Task InstallAsync(
            string minecraftVersion,
            string gameDirectory,
            IProgress<LauncherProgress>? progress,
            CancellationToken cancellationToken = default,
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return callback(minecraftVersion, gameDirectory);
        }
    }
}
