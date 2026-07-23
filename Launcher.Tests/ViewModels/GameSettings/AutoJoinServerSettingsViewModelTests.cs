/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Reflection;
using Launcher.App.Services;
using Launcher.App.ViewModels.GameSettings;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;
using Launcher.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class AutoJoinServerSettingsViewModelTests
{
    [Fact]
    public async Task GlobalEditorLoadsAndPersistsDefaultAutoJoinAddress()
    {
        var settings = new LauncherSettings
        {
            DefaultAutoJoinServerAddress = "old.example.com:25565"
        };
        var settingsService = new TestSettingsService(settings);
        using var persistence = new SettingsPersistenceCoordinator(
            settingsService,
            new RecordingStatusService(),
            NullLogger.Instance);
        persistence.Prime(settings);
        var viewModel = new LaunchMemorySettingsViewModel(persistence, new FixedSystemMemoryService());
        viewModel.Load(settings);

        Assert.Equal("old.example.com:25565", viewModel.DefaultAutoJoinServerAddress);

        viewModel.DefaultAutoJoinServerAddress = "  play.example.com:25565  ";
        await persistence.FlushAsync();

        Assert.Equal("play.example.com:25565", settings.DefaultAutoJoinServerAddress);
        Assert.Equal(1, settingsService.SaveCount);
    }

    [Fact]
    public async Task InstanceEditorShowsInheritedAddressThenPersistsOverride()
    {
        var globalSettings = new LauncherSettings
        {
            DefaultAutoJoinServerAddress = "global.example.com:25565"
        };
        var instance = CreateInstance(LaunchSettingsMode.UseGlobal, "stored.example.com:25566");
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        using var persistence = CreatePersistence(instanceService, new RecordingStatusService());
        persistence.SetInstance(instance);
        using var viewModel = CreateViewModel(persistence, globalSettings, instance);

        Assert.False(viewModel.AreLaunchSettingsOverridesEnabled);
        Assert.Equal("global.example.com:25565", viewModel.LaunchAutoJoinServerAddress);

        viewModel.SelectedLaunchSettingsModeOption = viewModel.LaunchSettingsModeOptions.Single(
            option => option.Mode is LaunchSettingsMode.PerInstance);
        viewModel.LaunchAutoJoinServerAddress = "instance.example.com:25567";
        await instanceService.SaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.AreLaunchSettingsOverridesEnabled);
        Assert.Equal(LaunchSettingsMode.PerInstance, instance.LaunchSettingsMode);
        Assert.Equal("instance.example.com:25567", instance.AutoJoinServerAddress);
    }

    [Fact]
    public async Task FailedInstanceSaveRollsBackAutoJoinAddressAndEditor()
    {
        var globalSettings = new LauncherSettings();
        var instance = CreateInstance(LaunchSettingsMode.PerInstance, "old.example.com:25565");
        var status = new RecordingStatusService();
        var instanceService = new FakeGameInstanceService
        {
            SaveException = new IOException("save failed")
        };
        instanceService.CreatedInstances.Add(instance);
        using var persistence = CreatePersistence(instanceService, status);
        persistence.SetInstance(instance);
        using var viewModel = CreateViewModel(persistence, globalSettings, instance);

        viewModel.LaunchAutoJoinServerAddress = "new.example.com:25566";
        await status.Message.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("old.example.com:25565", instance.AutoJoinServerAddress);
        Assert.Equal("old.example.com:25565", viewModel.LaunchAutoJoinServerAddress);
    }

    private static InstanceSettingsPersistenceCoordinator CreatePersistence(
        FakeGameInstanceService instanceService,
        RecordingStatusService statusService)
    {
        return new InstanceSettingsPersistenceCoordinator(
            instanceService,
            statusService,
            ImmediateUiDispatcher.Instance,
            NullLogger.Instance);
    }

    private static InstanceLaunchSettingsViewModel CreateViewModel(
        InstanceSettingsPersistenceCoordinator persistence,
        LauncherSettings globalSettings,
        GameInstance instance)
    {
        var viewModel = new InstanceLaunchSettingsViewModel(
            new FixedSystemMemoryService(),
            Stub<IModService>(),
            persistence);
        viewModel.PrimeFromSettings(globalSettings);
        viewModel.SetSelectedInstance(instance);
        return viewModel;
    }

    private static GameInstance CreateInstance(LaunchSettingsMode mode, string address)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "auto-join-test",
            MinecraftVersion = "1.20.1",
            VersionName = "auto-join-test",
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = Path.Combine(
                Path.GetTempPath(),
                "launcher-tests",
                Guid.NewGuid().ToString("N")),
            LaunchSettingsMode = mode,
            MemorySettingsMode = MemorySettingsMode.Manual,
            MemoryMb = 4096,
            AutoJoinServerAddress = address
        };
    }

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, DefaultInterfaceProxy>();

    private sealed class FixedSystemMemoryService : ISystemMemoryService
    {
        public SystemMemorySnapshot GetSnapshot()
            => new(16L * 1024 * 1024 * 1024, 8L * 1024 * 1024 * 1024);
    }

    private sealed class RecordingStatusService : IStatusService
    {
        public TaskCompletionSource<string> Message { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<string>? MessageReported;

        public void Report(string message)
        {
            Message.TrySetResult(message);
            MessageReported?.Invoke(message);
        }
    }

    public class DefaultInterfaceProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var returnType = targetMethod?.ReturnType;
            if (returnType is null || returnType == typeof(void))
                return null;
            if (returnType == typeof(Task))
                return Task.CompletedTask;
            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var resultType = returnType.GetGenericArguments()[0];
                var result = resultType.IsGenericType
                    && resultType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
                    ? Array.CreateInstance(resultType.GetGenericArguments()[0], 0)
                    : resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [result]);
            }

            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
