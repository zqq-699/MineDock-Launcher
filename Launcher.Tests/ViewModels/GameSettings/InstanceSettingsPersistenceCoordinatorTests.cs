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

using Launcher.App.Services;
using Launcher.App.Resources;
using Launcher.App.ViewModels.GameSettings;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class InstanceSettingsPersistenceCoordinatorTests
{
    [Fact]
    public async Task RapidChangesInSameAreaPersistOnlyLatestMutation()
    {
        var instance = CreateInstance("first");
        var instanceService = new FakeGameInstanceService();
        using var coordinator = CreateCoordinator(instanceService, new RecordingStatusService());
        coordinator.SetInstance(instance);

        coordinator.Schedule(
            "description",
            instance,
            target => ApplyDescription(target, "older"),
            () => { },
            TimeSpan.FromMilliseconds(80));
        coordinator.Schedule(
            "description",
            instance,
            target => ApplyDescription(target, "latest"),
            () => { },
            TimeSpan.FromMilliseconds(80));

        await WaitUntilAsync(() => instanceService.SaveCallCount == 1);

        Assert.Equal(1, instanceService.SaveCallCount);
        Assert.Equal("latest", instance.Description);
    }

    [Fact]
    public async Task SwitchingInstanceCancelsPendingMutationForPreviousInstance()
    {
        var first = CreateInstance("first");
        var second = CreateInstance("second");
        var instanceService = new FakeGameInstanceService();
        using var coordinator = CreateCoordinator(instanceService, new RecordingStatusService());
        coordinator.SetInstance(first);
        coordinator.Schedule(
            "description",
            first,
            target => ApplyDescription(target, "stale"),
            () => { },
            TimeSpan.FromMilliseconds(100));

        coordinator.SetInstance(second);
        coordinator.Schedule(
            "description",
            second,
            target => ApplyDescription(target, "current"),
            () => { },
            TimeSpan.FromMilliseconds(10));

        await WaitUntilAsync(() => instanceService.SaveCallCount == 1);
        await Task.Delay(150);

        Assert.Equal("first", first.Description);
        Assert.Equal("current", second.Description);
        Assert.Same(second, instanceService.LastSavedInstance);
    }

    [Fact]
    public async Task SaveFailureRollsBackModelAndReportsFriendlyStatus()
    {
        var instance = CreateInstance("original");
        var instanceService = new FakeGameInstanceService
        {
            SaveException = new IOException("technical details")
        };
        var statusService = new RecordingStatusService();
        using var coordinator = CreateCoordinator(instanceService, statusService);
        coordinator.SetInstance(instance);
        coordinator.Schedule(
            "description",
            instance,
            target => ApplyDescription(target, "changed"),
            () => { });

        var message = await statusService.Message.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("original", instance.Description);
        Assert.Equal(Strings.Status_InstanceSettingsSaveFailed, message);
        Assert.DoesNotContain("technical details", message, StringComparison.OrdinalIgnoreCase);
    }

    private static InstanceSettingsPersistenceCoordinator CreateCoordinator(
        FakeGameInstanceService instanceService,
        RecordingStatusService statusService)
    {
        return new InstanceSettingsPersistenceCoordinator(
            instanceService,
            statusService,
            ImmediateUiDispatcher.Instance,
            NullLogger.Instance);
    }

    private static GameInstance CreateInstance(string description)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Description = description,
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N"))
        };
    }

    private static Action? ApplyDescription(GameInstance instance, string description)
    {
        var original = instance.Description;
        if (string.Equals(original, description, StringComparison.Ordinal))
            return null;
        instance.Description = description;
        return () => instance.Description = original;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    private sealed class RecordingStatusService : IStatusService
    {
        public TaskCompletionSource<string> Message { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<string>? MessageReported;

        public void Report(string message)
        {
            Message.TrySetResult(message);
            MessageReported?.Invoke(message);
        }
    }
}
