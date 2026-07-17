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

using System.Reflection;
using Launcher.App.Services;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class RequiredSelectionPersistenceTests
{
    [Fact]
    public async Task GlobalSettingsRestoreRequiredSelectionsWithoutPersistingFallbacks()
    {
        var settings = new LauncherSettings
        {
            DownloadSourcePreference = DownloadSourcePreference.Official,
            LauncherLanguage = LauncherLanguages.English,
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = @"C:\Java\bin\java.exe",
            Theme = "Light",
            AccentColor = LauncherAccentColors.Purple,
            UpdateChannel = LauncherUpdateChannel.Beta
        };
        var settingsService = new TestSettingsService(settings);
        using var viewModel = CreateSettingsPage(settingsService);
        viewModel.PrimeFromSettings(settings);

        var downloadSource = viewModel.Download.SelectedDownloadSourceOption;
        var language = viewModel.Language.SelectedLanguageOption;
        var memoryMode = viewModel.LaunchMemory.SelectedMemoryModeOption;
        var javaMode = viewModel.Java.Editor.SelectedJavaSelectionOption;
        var theme = viewModel.Theme.SelectedThemeOption;
        var accent = viewModel.Theme.SelectedAccentColorOption;
        var updateChannel = viewModel.Info.SelectedUpdateChannelOption;

        viewModel.Download.SelectedDownloadSourceOption = null;
        viewModel.Language.SelectedLanguageOption = null;
        viewModel.LaunchMemory.SelectedMemoryModeOption = null;
        viewModel.Java.Editor.SelectedJavaSelectionOption = null;
        viewModel.Theme.SelectedThemeOption = null;
        viewModel.Theme.SelectedAccentColorOption = null;
        viewModel.Info.SelectedUpdateChannelOption = null;
        await viewModel.FlushPendingSettingsAsync();

        Assert.Same(downloadSource, viewModel.Download.SelectedDownloadSourceOption);
        Assert.Same(language, viewModel.Language.SelectedLanguageOption);
        Assert.Same(memoryMode, viewModel.LaunchMemory.SelectedMemoryModeOption);
        Assert.Same(javaMode, viewModel.Java.Editor.SelectedJavaSelectionOption);
        Assert.Same(theme, viewModel.Theme.SelectedThemeOption);
        Assert.Same(accent, viewModel.Theme.SelectedAccentColorOption);
        Assert.Same(updateChannel, viewModel.Info.SelectedUpdateChannelOption);
        Assert.Equal(DownloadSourcePreference.Official, settings.DownloadSourcePreference);
        Assert.Equal(LauncherLanguages.English, settings.LauncherLanguage);
        Assert.Equal(MemorySettingsMode.Manual, settings.DefaultMemorySettingsMode);
        Assert.Equal(JavaSelectionMode.Manual, settings.JavaSelectionMode);
        Assert.Equal("Light", settings.Theme);
        Assert.Equal(LauncherAccentColors.Purple, settings.AccentColor);
        Assert.Equal(LauncherUpdateChannel.Beta, settings.UpdateChannel);
        Assert.Equal(0, settingsService.SaveCount);
    }

    [Fact]
    public async Task ExplicitGlobalDefaultSelectionsStillPersist()
    {
        var settings = new LauncherSettings
        {
            DownloadSourcePreference = DownloadSourcePreference.BmclApi,
            LauncherLanguage = LauncherLanguages.English,
            DefaultMemorySettingsMode = MemorySettingsMode.Manual,
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = @"C:\Java\bin\java.exe",
            Theme = "Light",
            AccentColor = LauncherAccentColors.Purple,
            UpdateChannel = LauncherUpdateChannel.Beta
        };
        var settingsService = new TestSettingsService(settings);
        using var viewModel = CreateSettingsPage(settingsService);
        viewModel.PrimeFromSettings(settings);

        viewModel.Download.SelectedDownloadSourceOption = viewModel.Download.DownloadSourceOptions.Single(option =>
            option.Preference is DownloadSourcePreference.Official);
        viewModel.Language.SelectedLanguageOption = viewModel.Language.LanguageOptions.Single(option =>
            option.Id == LauncherLanguages.SimplifiedChinese);
        viewModel.LaunchMemory.SelectedMemoryModeOption = viewModel.LaunchMemory.MemoryModeOptions.Single(option =>
            option.Mode is MemorySettingsMode.Auto);
        viewModel.Java.Editor.SelectedJavaSelectionOption = viewModel.Java.Editor.JavaSelectionOptions.Single(option =>
            option.Id == "auto");
        viewModel.Theme.SelectedThemeOption = viewModel.Theme.ThemeOptions.Single(option =>
            option.Id == LauncherDefaults.DefaultTheme);
        viewModel.Theme.SelectedAccentColorOption = viewModel.Theme.AccentColorOptions.Single(option =>
            option.Id == LauncherDefaults.DefaultAccentColor);
        viewModel.Info.SelectedUpdateChannelOption = viewModel.Info.UpdateChannelOptions.Single(option =>
            option.Channel == LauncherDefaults.DefaultUpdateChannel);
        await viewModel.FlushPendingSettingsAsync();

        Assert.Equal(LauncherDefaults.DefaultDownloadSourcePreference, settings.DownloadSourcePreference);
        Assert.Equal(LauncherLanguages.SimplifiedChinese, settings.LauncherLanguage);
        Assert.Equal(MemorySettingsMode.Auto, settings.DefaultMemorySettingsMode);
        Assert.Equal(JavaSelectionMode.Auto, settings.JavaSelectionMode);
        Assert.Equal(LauncherDefaults.DefaultTheme, settings.Theme);
        Assert.Equal(LauncherDefaults.DefaultAccentColor, settings.AccentColor);
        Assert.Equal(LauncherDefaults.DefaultUpdateChannel, settings.UpdateChannel);
        Assert.True(settingsService.SaveCount > 0);
    }

    [Fact]
    public async Task InstanceEditorsRestoreRequiredSelectionsWithoutDowngradingModes()
    {
        var instance = CreateInstance(
            LaunchSettingsMode.PerInstance,
            LaunchSettingsMode.PerInstance);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        using var persistence = CreateInstancePersistence(instanceService);
        persistence.SetInstance(instance);
        using var launch = CreateLaunchEditor(persistence, instance);
        using var java = CreateJavaEditor(persistence, instance);

        var launchMode = launch.SelectedLaunchSettingsModeOption;
        var memoryMode = launch.SelectedMemoryModeOption;
        var javaMode = java.SelectedInstanceJavaSettingsModeOption;
        var javaSelection = java.InstanceJavaSettings.SelectedJavaSelectionOption;

        launch.SelectedLaunchSettingsModeOption = null;
        launch.SelectedMemoryModeOption = null;
        java.SelectedInstanceJavaSettingsModeOption = null;
        java.InstanceJavaSettings.SelectedJavaSelectionOption = null;
        await Task.Delay(200);

        Assert.Same(launchMode, launch.SelectedLaunchSettingsModeOption);
        Assert.Same(memoryMode, launch.SelectedMemoryModeOption);
        Assert.Same(javaMode, java.SelectedInstanceJavaSettingsModeOption);
        Assert.Same(javaSelection, java.InstanceJavaSettings.SelectedJavaSelectionOption);
        Assert.True(launch.AreLaunchSettingsOverridesEnabled);
        Assert.True(java.AreInstanceJavaSettingsOverridesEnabled);
        Assert.Equal(LaunchSettingsMode.PerInstance, instance.LaunchSettingsMode);
        Assert.Equal(LaunchSettingsMode.PerInstance, instance.JavaSettingsMode);
        Assert.Equal(0, instanceService.SaveCallCount);
    }

    [Fact]
    public async Task PendingPerInstanceLaunchSelectionSurvivesTransientNull()
    {
        var instance = CreateInstance(LaunchSettingsMode.UseGlobal, LaunchSettingsMode.UseGlobal);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        using var persistence = CreateInstancePersistence(instanceService);
        persistence.SetInstance(instance);
        using var launch = CreateLaunchEditor(persistence, instance);

        launch.SelectedLaunchSettingsModeOption = launch.LaunchSettingsModeOptions.Single(option =>
            option.Mode is LaunchSettingsMode.PerInstance);
        launch.SelectedLaunchSettingsModeOption = null;
        await instanceService.SaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(LaunchSettingsMode.PerInstance, launch.SelectedLaunchSettingsModeOption?.Mode);
        Assert.Equal(LaunchSettingsMode.PerInstance, instance.LaunchSettingsMode);
        Assert.Equal(1, instanceService.SaveCallCount);
    }

    [Fact]
    public async Task PendingPerInstanceJavaSelectionSurvivesTransientNull()
    {
        var instance = CreateInstance(LaunchSettingsMode.UseGlobal, LaunchSettingsMode.UseGlobal);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        using var persistence = CreateInstancePersistence(instanceService);
        persistence.SetInstance(instance);
        using var java = CreateJavaEditor(persistence, instance);

        java.SelectedInstanceJavaSettingsModeOption = java.LaunchSettingsModeOptions.Single(option =>
            option.Mode is LaunchSettingsMode.PerInstance);
        java.SelectedInstanceJavaSettingsModeOption = null;
        await instanceService.SaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(LaunchSettingsMode.PerInstance, java.SelectedInstanceJavaSettingsModeOption?.Mode);
        Assert.Equal(LaunchSettingsMode.PerInstance, instance.JavaSettingsMode);
        Assert.Equal(1, instanceService.SaveCallCount);
    }

    [Fact]
    public async Task ExplicitUseGlobalSelectionStillPersists()
    {
        var instance = CreateInstance(LaunchSettingsMode.PerInstance, LaunchSettingsMode.PerInstance);
        var instanceService = new FakeGameInstanceService();
        instanceService.CreatedInstances.Add(instance);
        using var persistence = CreateInstancePersistence(instanceService);
        persistence.SetInstance(instance);
        using var launch = CreateLaunchEditor(persistence, instance);

        launch.SelectedLaunchSettingsModeOption = launch.LaunchSettingsModeOptions.Single(option =>
            option.Mode is LaunchSettingsMode.UseGlobal);
        await instanceService.SaveCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(LaunchSettingsMode.UseGlobal, instance.LaunchSettingsMode);
        Assert.Equal(1, instanceService.SaveCallCount);
    }

    private static SettingsPageViewModel CreateSettingsPage(ISettingsService settingsService)
    {
        return new SettingsPageViewModel(
            settingsService,
            Stub<IStatusService>(),
            Stub<ISystemMemoryService>(),
            Stub<IJavaRuntimeDiscoveryService>(),
            Stub<IFilePickerService>(),
            Stub<IInstanceFolderService>(),
            Stub<IFloatingMessageService>(),
            Stub<IThemeService>(),
            Stub<IExternalLinkService>(),
            Stub<ILauncherUpdateService>(),
            Stub<ILauncherSelfUpdateService>(),
            Stub<IApplicationExitService>());
    }

    private static InstanceSettingsPersistenceCoordinator CreateInstancePersistence(
        IGameInstanceService instanceService)
    {
        return new InstanceSettingsPersistenceCoordinator(
            instanceService,
            Stub<IStatusService>(),
            ImmediateUiDispatcher.Instance,
            NullLogger.Instance);
    }

    private static InstanceLaunchSettingsViewModel CreateLaunchEditor(
        InstanceSettingsPersistenceCoordinator persistence,
        GameInstance instance)
    {
        var editor = new InstanceLaunchSettingsViewModel(
            Stub<ISystemMemoryService>(),
            Stub<IModService>(),
            persistence);
        editor.PrimeFromSettings(CreateGlobalSettings());
        editor.SetSelectedInstance(instance);
        return editor;
    }

    private static InstanceJavaSettingsViewModel CreateJavaEditor(
        InstanceSettingsPersistenceCoordinator persistence,
        GameInstance instance)
    {
        var editor = new InstanceJavaSettingsViewModel(
            persistence,
            Stub<IJavaRuntimeDiscoveryService>(),
            Stub<IStatusService>(),
            Stub<IFilePickerService>(),
            Stub<IFloatingMessageService>());
        editor.PrimeFromSettings(CreateGlobalSettings());
        editor.SetSelectedInstance(instance);
        return editor;
    }

    private static LauncherSettings CreateGlobalSettings()
    {
        return new LauncherSettings
        {
            DefaultMemorySettingsMode = MemorySettingsMode.Auto,
            DefaultMemoryMb = 4096,
            JavaSelectionMode = JavaSelectionMode.Auto
        };
    }

    private static GameInstance CreateInstance(
        LaunchSettingsMode launchSettingsMode,
        LaunchSettingsMode javaSettingsMode)
    {
        return new GameInstance
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "selection-test",
            MinecraftVersion = "1.21.1",
            VersionName = "selection-test",
            Loader = LoaderKind.Vanilla,
            InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", Guid.NewGuid().ToString("N")),
            LaunchSettingsMode = launchSettingsMode,
            MemorySettingsMode = MemorySettingsMode.Manual,
            MemoryMb = 6144,
            JavaSettingsMode = javaSettingsMode,
            JavaSelectionMode = JavaSelectionMode.Manual,
            SelectedJavaExecutablePath = @"C:\Java\bin\java.exe"
        };
    }

    private static T Stub<T>() where T : class
    {
        return DispatchProxy.Create<T, DefaultInterfaceProxy>();
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
                var result = CreateDefaultResult(resultType);
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, [result]);
            }

            return CreateDefaultResult(returnType);
        }

        private static object? CreateDefaultResult(Type resultType)
        {
            if (resultType.IsGenericType
                && resultType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                return Array.CreateInstance(resultType.GetGenericArguments()[0], 0);
            }

            return resultType.IsValueType ? Activator.CreateInstance(resultType) : null;
        }
    }
}
