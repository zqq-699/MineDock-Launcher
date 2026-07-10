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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed partial class DownloadVersionListViewModel : ObservableObject, IDisposable
{
    public const string LocalImportCategoryId = "local_import";
    private readonly IGameVersionService gameVersionService;
    private readonly IUiDispatcher uiDispatcher;
    private CancellationTokenSource? loadCancellation;
    private Task? loadTask;
    private bool hasLoadedVersions;
    private int refreshRequestVersion;
    private DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto;
    private int downloadSpeedLimitMbPerSecond;

    [ObservableProperty]
    private DownloadVersionCategory? selectedVersionCategory;

    [ObservableProperty]
    private DownloadMinecraftVersionItem? selectedMinecraftVersion;

    [ObservableProperty]
    private bool isLoadingVersions;

    [ObservableProperty]
    private string versionLoadError = string.Empty;

    [ObservableProperty]
    private string versionEmptyMessage = string.Empty;

    [ObservableProperty]
    private string versionSearchQuery = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DownloadMinecraftVersionItem> visibleVersions = Array.Empty<DownloadMinecraftVersionItem>();

    [ObservableProperty]
    private int listEntranceAnimationToken;

    public DownloadVersionListViewModel(
        IGameVersionService gameVersionService,
        IUiDispatcher uiDispatcher)
    {
        this.gameVersionService = gameVersionService;
        this.uiDispatcher = uiDispatcher;
        VersionCategories.Add(new DownloadVersionCategory("release", Strings.Download_ReleaseCategory, string.Empty, "instance_download_page/release"));
        VersionCategories.Add(new DownloadVersionCategory("snapshot", Strings.Download_SnapshotCategory, string.Empty, "instance_download_page/snapshot"));
        VersionCategories.Add(new DownloadVersionCategory("old_beta", Strings.Download_BetaCategory, "\u03b2"));
        VersionCategories.Add(new DownloadVersionCategory("old_alpha", Strings.Download_AlphaCategory, "\u03b1"));
        VersionCategories.Add(new DownloadVersionCategory(
            LocalImportCategoryId,
            Strings.Download_LocalImportCategory,
            string.Empty,
            "instance_download_page/localimport",
            isEnabled: true));
        SelectVersionCategoryCore(VersionCategories[0], deferRefresh: false);
    }

    public event Action<DownloadMinecraftVersionItem>? VersionSelected;

    public event Action? LocalImportRequested;

    public event Action? CategoryContentRefreshRequested;

    public ObservableCollection<DownloadVersionCategory> VersionCategories { get; } = [];

    public List<DownloadMinecraftVersionItem> AllVersions { get; } = [];

    public bool HasVisibleVersions => VisibleVersions.Count > 0;

    public bool HasSelectedMinecraftVersion => SelectedMinecraftVersion is not null;

    public bool HasVersionLoadError => !string.IsNullOrWhiteSpace(VersionLoadError);

    public bool HasVersionEmptyMessage => !string.IsNullOrWhiteSpace(VersionEmptyMessage);

    public void ApplyDownloadSourcePreference(DownloadSourcePreference preference)
    {
        if (downloadSourcePreference == preference)
            return;

        downloadSourcePreference = preference;
        CancelLoad();
        hasLoadedVersions = false;
        IsLoadingVersions = false;
        VersionLoadError = string.Empty;
        VersionEmptyMessage = string.Empty;
        AllVersions.Clear();
        ClearSelectedVersion();
        RefreshVisibleVersions();
    }

    public void ApplyDownloadSpeedLimit(int value)
    {
        downloadSpeedLimitMbPerSecond = Math.Max(value, 0);
    }

    public async Task EnsureVersionsLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (hasLoadedVersions)
            return;

        if (loadTask is not null)
        {
            await loadTask.WaitAsync(cancellationToken);
            return;
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        loadCancellation = cancellation;
        var currentTask = LoadVersionsAsync(cancellation);
        loadTask = currentTask;
        try
        {
            await currentTask;
        }
        finally
        {
            if (ReferenceEquals(loadTask, currentTask))
                loadTask = null;
            if (ReferenceEquals(loadCancellation, cancellation))
                loadCancellation = null;
            cancellation.Dispose();
        }
    }

    public void RefreshCurrentCategoryContent()
    {
        RequestVisibleVersionsRefresh(defer: false);
    }

    public void ClearSelectedVersion()
    {
        SelectedMinecraftVersion = null;
        foreach (var item in AllVersions)
            item.IsSelected = false;
    }

    public void Dispose()
    {
        CancelLoad();
    }

    [RelayCommand]
    private void SelectVersionCategory(DownloadVersionCategory category)
    {
        if (!category.IsEnabled)
            return;

        if (string.Equals(category.Id, LocalImportCategoryId, StringComparison.Ordinal))
        {
            LocalImportRequested?.Invoke();
            return;
        }

        var refreshCurrentCategory = ReferenceEquals(SelectedVersionCategory, category);
        SelectVersionCategoryCore(category, deferRefresh: false);
        if (refreshCurrentCategory)
        {
            RefreshCurrentCategoryContent();
            CategoryContentRefreshRequested?.Invoke();
        }
        else if (hasLoadedVersions)
            ListEntranceAnimationToken++;
    }

    [RelayCommand]
    private void SelectMinecraftVersion(DownloadMinecraftVersionItem version)
    {
        SelectedMinecraftVersion = version;
        foreach (var item in AllVersions)
            item.IsSelected = ReferenceEquals(item, version);
        VersionSelected?.Invoke(version);
    }

    partial void OnVersionSearchQueryChanged(string value)
    {
        RequestVisibleVersionsRefresh(defer: false);
    }

    partial void OnVersionLoadErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasVersionLoadError));
    }

    partial void OnVersionEmptyMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasVersionEmptyMessage));
    }

    partial void OnSelectedMinecraftVersionChanged(DownloadMinecraftVersionItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedMinecraftVersion));
    }

    partial void OnVisibleVersionsChanged(IReadOnlyList<DownloadMinecraftVersionItem> value)
    {
        OnPropertyChanged(nameof(HasVisibleVersions));
        OnPropertyChanged(nameof(HasVersionEmptyMessage));
    }

    private async Task LoadVersionsAsync(CancellationTokenSource cancellation)
    {
        IsLoadingVersions = true;
        VersionLoadError = string.Empty;
        VersionEmptyMessage = string.Empty;
        try
        {
            var versions = await gameVersionService.GetVersionsAsync(
                downloadSourcePreference,
                cancellation.Token,
                downloadSpeedLimitMbPerSecond);
            if (!ReferenceEquals(loadCancellation, cancellation))
                return;

            AllVersions.Clear();
            AllVersions.AddRange(versions.Select(version => new DownloadMinecraftVersionItem(version)));
            hasLoadedVersions = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (ReferenceEquals(loadCancellation, cancellation))
                VersionLoadError = Strings.Status_LoadVersionsFailed;
        }
        finally
        {
            if (ReferenceEquals(loadCancellation, cancellation))
            {
                IsLoadingVersions = false;
                RefreshVisibleVersions();
                if (hasLoadedVersions)
                    ListEntranceAnimationToken++;
            }
        }
    }

    private void SelectVersionCategoryCore(DownloadVersionCategory category, bool deferRefresh)
    {
        SelectedVersionCategory = category;
        foreach (var item in VersionCategories)
            item.IsSelected = ReferenceEquals(item, category);
        RequestVisibleVersionsRefresh(deferRefresh);
    }

    private void RequestVisibleVersionsRefresh(bool defer)
    {
        var requestVersion = ++refreshRequestVersion;
        if (defer && uiDispatcher.HasAccess)
        {
            uiDispatcher.Post(() =>
            {
                if (requestVersion == refreshRequestVersion)
                    RefreshVisibleVersions();
            });
            return;
        }

        RefreshVisibleVersions();
    }

    private void RefreshVisibleVersions()
    {
        var result = DownloadVersionFilter.Apply(
            AllVersions,
            SelectedVersionCategory,
            VersionSearchQuery,
            SelectedMinecraftVersion,
            hasLoadedVersions,
            IsLoadingVersions,
            HasVersionLoadError);
        VersionEmptyMessage = result.EmptyMessage;
        if (result.ShouldClearSelectedVersion)
            ClearSelectedVersion();
        VisibleVersions = result.Versions;
    }

    private void CancelLoad()
    {
        loadCancellation?.Cancel();
        loadCancellation = null;
        loadTask = null;
    }
}
