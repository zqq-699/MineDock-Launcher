/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class GameSettingsEditDialogViewModelTests
{
    [Fact]
    public async Task RenameOnlyPreservesExternalInstanceIcon()
    {
        const string iconSource = "file:///C:/Minecraft/versions/Pack/BHL/resource-project-icon.png";
        var instance = new GameInstance
        {
            Id = "instance",
            Name = "Pack",
            VersionName = "Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = @"C:\Minecraft\versions\Pack",
            IconSource = iconSource
        };
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        var viewModel = new GameSettingsEditDialogViewModel(instanceService, new NullStatusService());

        viewModel.Open(new GameSettingsInstanceItem(instance, "release"));

        Assert.Equal(iconSource, viewModel.SelectedIconOption?.IconSource);
        Assert.Contains(viewModel.IconOptions, option => option.IconSource == iconSource);
        viewModel.InstanceName = "Renamed Pack";
        await viewModel.ConfirmAsync();

        Assert.Equal(iconSource, instanceService.LastRenamedIconSource);
    }

    [Fact]
    public async Task SelectingBuiltInIconReplacesExternalInstanceIcon()
    {
        const string iconSource = "file:///C:/Minecraft/versions/Pack/BHL/resource-project-icon.png";
        var instance = new GameInstance
        {
            Id = "instance",
            Name = "Pack",
            VersionName = "Pack",
            MinecraftVersion = "1.20.1",
            InstanceDirectory = @"C:\Minecraft\versions\Pack",
            IconSource = iconSource
        };
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        var viewModel = new GameSettingsEditDialogViewModel(instanceService, new NullStatusService());
        viewModel.Open(new GameSettingsInstanceItem(instance, "release"));
        var builtInIcon = viewModel.IconOptions.First(option => option.IconSource.StartsWith("/Assets/", StringComparison.Ordinal));

        viewModel.SelectedIconOption = builtInIcon;
        await viewModel.ConfirmAsync();

        Assert.Equal(builtInIcon.IconSource, instanceService.LastRenamedIconSource);
    }

    private sealed class NullStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message) => MessageReported?.Invoke(message);
    }
}
