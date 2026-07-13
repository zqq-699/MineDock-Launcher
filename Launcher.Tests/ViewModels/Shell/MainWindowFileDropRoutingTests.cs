/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Models;

namespace Launcher.Tests.ViewModels.Shell;

public sealed class MainWindowFileDropRoutingTests
{
    [Theory]
    [InlineData(NavigationCatalog.AccountPage)]
    [InlineData(NavigationCatalog.HomePage)]
    [InlineData(NavigationCatalog.DownloadPage)]
    [InlineData(NavigationCatalog.InstallPage)]
    [InlineData(NavigationCatalog.ResourcesPage)]
    [InlineData(NavigationCatalog.SettingsPage)]
    public void TopLevelPagesUseLocalModpackDrop(string page)
    {
        Assert.True(NavigationCatalog.UsesLocalModpackDrop(page, isGameSettingsListStep: false));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void GameSettingsUsesLocalModpackDropOnlyForListStep(
        bool isGameSettingsListStep,
        bool expected)
    {
        Assert.Equal(
            expected,
            NavigationCatalog.UsesLocalModpackDrop(
                NavigationCatalog.GameSettingsPage,
                isGameSettingsListStep));
    }

    [Theory]
    [InlineData("General")]
    [InlineData(null)]
    public void UnknownPagesDoNotUseLocalModpackDrop(string? page)
    {
        Assert.False(NavigationCatalog.UsesLocalModpackDrop(page, isGameSettingsListStep: true));
    }
}
