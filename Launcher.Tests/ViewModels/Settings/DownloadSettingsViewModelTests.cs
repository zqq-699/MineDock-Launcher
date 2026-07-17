/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using Launcher.App.Services;
using Launcher.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.Tests.ViewModels.Settings;

public sealed class DownloadSettingsViewModelTests
{
    [Fact]
    public void DownloadSourceOptionsContainOnlyOfficialAndBmclApiWithOfficialSelectedByDefault()
    {
        var settings = new LauncherSettings();
        using var persistence = new SettingsPersistenceCoordinator(
            new TestSettingsService(settings),
            new RecordingStatusService(),
            NullLogger.Instance);
        persistence.Prime(settings);
        var viewModel = new DownloadSettingsViewModel(persistence);

        viewModel.Load(settings);

        Assert.Equal(
            [DownloadSourcePreference.Official, DownloadSourcePreference.BmclApi],
            viewModel.DownloadSourceOptions.Select(option => option.Preference));
        Assert.Equal(DownloadSourcePreference.Official, viewModel.SelectedDownloadSourceOption?.Preference);
    }

    [Fact]
    public async Task MaximumDownloadConcurrencyPersistsAndRaisesRuntimeEvent()
    {
        var settings = new LauncherSettings
        {
            MaximumDownloadConcurrency = LauncherDefaults.DefaultMaximumDownloadConcurrency
        };
        var service = new TestSettingsService(settings);
        using var persistence = new SettingsPersistenceCoordinator(
            service,
            new RecordingStatusService(),
            NullLogger.Instance);
        persistence.Prime(settings);
        var viewModel = new DownloadSettingsViewModel(persistence);
        viewModel.Load(settings);
        SettingsMaximumDownloadConcurrencyChangedEventArgs? change = null;
        viewModel.MaximumDownloadConcurrencyChanged += (_, args) => change = args;

        viewModel.MaximumDownloadConcurrency = 96;
        await persistence.FlushAsync();

        Assert.Equal(96, settings.MaximumDownloadConcurrency);
        Assert.Equal(96, change?.MaximumDownloadConcurrency);
        Assert.Equal(1, service.SaveCount);
    }

    [Theory]
    [InlineData(0, LauncherDefaults.MinimumDownloadConcurrency)]
    [InlineData(256, LauncherDefaults.MaximumDownloadConcurrency)]
    public void MaximumDownloadConcurrencyIsClampedToSliderRange(int value, int expected)
    {
        var settings = new LauncherSettings();
        using var persistence = new SettingsPersistenceCoordinator(
            new TestSettingsService(settings),
            new RecordingStatusService(),
            NullLogger.Instance);
        persistence.Prime(settings);
        var viewModel = new DownloadSettingsViewModel(persistence);
        viewModel.Load(settings);

        viewModel.MaximumDownloadConcurrency = value;

        Assert.Equal(expected, viewModel.MaximumDownloadConcurrency);
        Assert.Equal(expected, settings.MaximumDownloadConcurrency);
    }

    private sealed class RecordingStatusService : IStatusService
    {
        public event Action<string>? MessageReported;

        public void Report(string message) => MessageReported?.Invoke(message);
    }
}
