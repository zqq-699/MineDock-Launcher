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

using Launcher.App.ViewModels.GameSettings;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.ViewModels.GameSettings;

public sealed class GameSettingsInstanceListViewModelTests
{
    [Fact]
    public async Task ListOwnsLoadingClassificationAndCategoryFiltering()
    {
        var instances = new FakeGameInstanceService();
        instances.CreatedInstances.AddRange(
        [
            CreateInstance("release", "1.21", LoaderKind.Vanilla),
            CreateInstance("modded", "1.20.1", LoaderKind.Forge)
        ]);
        var viewModel = new GameSettingsInstanceListViewModel(
            instances,
            new StubGameVersionService(
            [
                new MinecraftVersionInfo("1.21", "release", false),
                new MinecraftVersionInfo("1.20.1", "release", false)
            ]));

        await viewModel.EnsureLoadedAsync();
        viewModel.SelectCategory(viewModel.Categories.Single(category => category.Id == "mod_loader"));

        Assert.Equal(2, viewModel.AllInstances.Count);
        Assert.Equal("modded", Assert.Single(viewModel.VisibleInstances).Name);
        Assert.False(viewModel.HasLoadError);
    }

    [Fact]
    public async Task DetailsCanPreserveSelectionWhileListFilterHidesIt()
    {
        var instances = new FakeGameInstanceService();
        instances.CreatedInstances.Add(CreateInstance("release", "1.21", LoaderKind.Vanilla));
        var viewModel = new GameSettingsInstanceListViewModel(
            instances,
            new StubGameVersionService([new MinecraftVersionInfo("1.21", "release", false)]));
        await viewModel.EnsureLoadedAsync();
        var selected = viewModel.SelectInstance(Assert.Single(viewModel.VisibleInstances));

        viewModel.SetPreserveFilteredSelection(true);
        viewModel.SearchQuery = "not-present";

        Assert.Same(selected, viewModel.SelectedInstance);
        Assert.Empty(viewModel.VisibleInstances);

        viewModel.SetPreserveFilteredSelection(false);
        Assert.Null(viewModel.SelectedInstance);
    }

    [Fact]
    public async Task InPlaceUpdateRefreshesDisplayWithoutRefreshingSelectedInstanceContext()
    {
        var instance = CreateInstance("release", "1.21", LoaderKind.Vanilla);
        var instances = new FakeGameInstanceService();
        instances.CreatedInstances.Add(instance);
        var viewModel = new GameSettingsInstanceListViewModel(
            instances,
            new StubGameVersionService([new MinecraftVersionInfo("1.21", "release", false)]));
        await viewModel.EnsureLoadedAsync();
        var selected = viewModel.SelectInstance(Assert.Single(viewModel.VisibleInstances));
        var selectedInstanceNotifications = 0;
        var displayNotifications = new List<string?>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameSettingsInstanceListViewModel.SelectedInstance))
                selectedInstanceNotifications++;
        };
        selected.PropertyChanged += (_, e) => displayNotifications.Add(e.PropertyName);

        instance.Name = "updated";
        viewModel.AddOrUpdate(instance);

        Assert.Equal(0, selectedInstanceNotifications);
        Assert.Equal("updated", selected.Name);
        Assert.Contains(nameof(GameSettingsInstanceItem.Name), displayNotifications);
    }

    [Fact]
    public async Task ReplacementUpdateRefreshesSelectedInstanceContext()
    {
        var original = CreateInstance("release", "1.21", LoaderKind.Vanilla);
        var instances = new FakeGameInstanceService();
        instances.CreatedInstances.Add(original);
        var viewModel = new GameSettingsInstanceListViewModel(
            instances,
            new StubGameVersionService([new MinecraftVersionInfo("1.21", "release", false)]));
        await viewModel.EnsureLoadedAsync();
        var selected = viewModel.SelectInstance(Assert.Single(viewModel.VisibleInstances));
        var selectedInstanceNotifications = 0;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameSettingsInstanceListViewModel.SelectedInstance))
                selectedInstanceNotifications++;
        };
        var replacement = CreateInstance("replacement", "1.21", LoaderKind.Vanilla);
        replacement.Id = original.Id;

        viewModel.AddOrUpdate(replacement);

        Assert.Equal(1, selectedInstanceNotifications);
        Assert.Same(selected, viewModel.SelectedInstance);
        Assert.Same(replacement, selected.Instance);
    }

    [Fact]
    public async Task RefreshTakesInstanceSnapshotAfterSlowVersionClassificationLoad()
    {
        var original = CreateInstance("original", "1.21", LoaderKind.Vanilla);
        var latest = CreateInstance("latest", "1.21", LoaderKind.Vanilla);
        latest.Id = original.Id;
        var instances = new FakeGameInstanceService();
        instances.CreatedInstances.Add(original);
        var versions = new CoordinatedGameVersionService();
        var viewModel = new GameSettingsInstanceListViewModel(instances, versions);

        var refresh = viewModel.EnsureLoadedAsync();
        await versions.LoadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        instances.CreatedInstances.Clear();
        instances.CreatedInstances.Add(latest);
        versions.ReleaseLoad.TrySetResult(true);
        await refresh;

        Assert.Same(latest, Assert.Single(viewModel.AllInstances).Instance);
    }

    [Fact]
    public async Task ImportBatchDeduplicatesPathsAndStopsAtFirstFailure()
    {
        var visited = new List<string>();

        var result = await LocalContentImportBatchCoordinator.ExecuteAsync(
            ["first.zip", "FIRST.zip", "failed.zip", "never.zip"],
            path =>
            {
                visited.Add(path);
                return Task.FromResult(new ImportResult(!path.Equals("failed.zip", StringComparison.OrdinalIgnoreCase)));
            },
            item => item.IsSuccess);

        Assert.Equal(["first.zip", "failed.zip"], visited);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal("failed.zip", result.FailedPath);
        Assert.NotNull(result.Failure);
    }

    private static GameInstance CreateInstance(string name, string version, LoaderKind loader) => new()
    {
        Id = name,
        Name = name,
        MinecraftVersion = version,
        VersionName = version,
        Loader = loader,
        InstanceDirectory = Path.Combine(Path.GetTempPath(), "launcher-tests", name)
    };

    private sealed record ImportResult(bool IsSuccess);

    private sealed class StubGameVersionService(IReadOnlyList<MinecraftVersionInfo> versions) : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0) => Task.FromResult(versions);
    }

    private sealed class CoordinatedGameVersionService : IGameVersionService
    {
        public TaskCompletionSource<bool> LoadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> ReleaseLoad { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            LoadStarted.TrySetResult(true);
            await ReleaseLoad.Task.WaitAsync(cancellationToken);
            return [new MinecraftVersionInfo("1.21", "release", false)];
        }
    }
}
