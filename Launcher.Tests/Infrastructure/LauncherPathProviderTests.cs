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

using Launcher.Application;
using Launcher.Infrastructure;

namespace Launcher.Tests.Infrastructure;

public sealed class LauncherPathProviderTests : TestTempDirectory
{
    [Fact]
    public void DefaultDataDirectoryUsesFixedApplicationIdBesideLauncher()
    {
        var pathProvider = new LauncherPathProvider(applicationBaseDirectory: TempRoot);

        Assert.Equal("BHL", LauncherApplicationIdentity.StorageDirectoryName);
        Assert.Equal(Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName), pathProvider.DefaultDataDirectory);
        Assert.Equal(LauncherApplicationIdentity.StorageDirectoryName, pathProvider.ApplicationId);
    }

    [Fact]
    public void DefaultDataDirectoryDoesNotDependOnExecutableName()
    {
        var firstProvider = new LauncherPathProvider(applicationBaseDirectory: TempRoot);
        var secondProvider = new LauncherPathProvider(applicationBaseDirectory: TempRoot);

        Assert.Equal(firstProvider.DefaultDataDirectory, secondProvider.DefaultDataDirectory);
        Assert.Equal(Path.Combine(TempRoot, LauncherApplicationIdentity.StorageDirectoryName), firstProvider.DefaultDataDirectory);
    }

    [Fact]
    public void DefaultAccountDataDirectoryUsesApplicationDataFolder()
    {
        var appDataRoot = Path.Combine(TempRoot, "roaming");
        var pathProvider = new LauncherPathProvider(
            applicationBaseDirectory: Path.Combine(TempRoot, "app"),
            applicationDataDirectory: appDataRoot);

        Assert.Equal(
            Path.Combine(appDataRoot, LauncherApplicationIdentity.StorageDirectoryName, "accounts"),
            pathProvider.DefaultAccountDataDirectory);
    }
}
