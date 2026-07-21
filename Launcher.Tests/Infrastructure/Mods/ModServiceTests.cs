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

using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application;
using Launcher.Domain.Models;
using Launcher.Infrastructure;
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
        var service = CreateService();

        var imported = await service.ImportAsync(instance, sourceJar);
        await service.SetEnabledAsync(imported, false);
        var disabled = (await service.GetModsAsync(instance)).Single();

        Assert.False(disabled.IsEnabled);
        Assert.Equal("example.jar.disabled", disabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar.disabled")));

        await service.SetEnabledAsync(disabled, true);
        var enabled = (await service.GetModsAsync(instance)).Single();

        Assert.True(enabled.IsEnabled);
        Assert.Equal("example.jar", enabled.FileName);
        Assert.True(File.Exists(Path.Combine(instanceDirectory, "mods", "example.jar")));
    }

    [Fact]
    public async Task ModServiceImportAsyncOverwritesExistingJarWhenRequested()
    {
        var instanceDirectory = Path.Combine(TempRoot, "instances", "overwrite");
        Directory.CreateDirectory(instanceDirectory);
        var sourceJar = Path.Combine(TempRoot, "replace-me.jar");
        await File.WriteAllTextAsync(sourceJar, "first");

        var instance = new GameInstance { InstanceDirectory = instanceDirectory };
        var service = CreateService();

        await service.ImportAsync(instance, sourceJar);
        await File.WriteAllTextAsync(sourceJar, "second");

        await service.ImportAsync(instance, sourceJar, overwriteExisting: true);

        var importedPath = Path.Combine(instanceDirectory, "mods", "replace-me.jar");
        Assert.Equal("second", await File.ReadAllTextAsync(importedPath));
        Assert.Single(await service.GetModsAsync(instance));
    }

    private ModService CreateService()
    {
        return new ModService(new LauncherPathProvider(TempRoot));
    }

    private static (string EntryName, byte[] Content) TextEntry(string entryName, string content)
    {
        return (entryName, Encoding.UTF8.GetBytes(content));
    }

}
