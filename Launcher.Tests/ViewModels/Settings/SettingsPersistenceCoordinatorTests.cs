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

using Microsoft.Extensions.Logging.Abstractions;
using Launcher.App.Services;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class SettingsPersistenceCoordinatorTests
{
    [Fact]
    public async Task RapidUpdatesAreMergedIntoOneSave()
    {
        var settings = new LauncherSettings();
        var service = new TestSettingsService(settings);
        using var coordinator = new SettingsPersistenceCoordinator(
            service,
            new RecordingStatusService(),
            NullLogger.Instance);
        coordinator.Prime(settings);

        coordinator.Update(value => value.Theme = "Light");
        coordinator.Update(value => value.AccentColor = "Purple");

        await service.SaveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, service.SaveCount);
        Assert.Equal("Light", settings.Theme);
        Assert.Equal("Purple", settings.AccentColor);
    }

    [Fact]
    public async Task ImmediateSavePersistsPendingUpdatesOnce()
    {
        var settings = new LauncherSettings();
        var service = new TestSettingsService(settings);
        using var coordinator = new SettingsPersistenceCoordinator(
            service,
            new RecordingStatusService(),
            NullLogger.Instance);
        coordinator.Prime(settings);
        coordinator.Update(value => value.Theme = "Light");

        await coordinator.SaveImmediatelyAsync(value => value.MinecraftDirectory = "C:\\Games\\Minecraft");

        Assert.Equal(1, service.SaveCount);
        Assert.Equal("Light", settings.Theme);
        Assert.Equal("C:\\Games\\Minecraft", settings.MinecraftDirectory);
    }

    private sealed class RecordingStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message)
        {
            MessageReported?.Invoke(message);
        }
    }
}
