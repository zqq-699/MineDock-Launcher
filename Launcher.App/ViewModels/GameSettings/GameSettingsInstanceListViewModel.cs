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
using Launcher.App.Resources;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsInstanceListViewModel : ObservableObject
{
    private readonly IGameInstanceService instanceService;
    private readonly IGameVersionService gameVersionService;
    private readonly ILogger<GameSettingsInstanceListViewModel> logger;
    private IReadOnlyDictionary<string, string> versionTypesByName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private bool hasLoadedInstances;
    private bool preserveFilteredSelection;

    [ObservableProperty]
    private GameSettingsInstanceCategory? selectedCategory;

    [ObservableProperty]
    private GameSettingsInstanceItem? selectedInstance;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string loadError = string.Empty;

    [ObservableProperty]
    private string emptyMessage = string.Empty;

    [ObservableProperty]
    private string searchQuery = string.Empty;

    [ObservableProperty]
    private int entranceAnimationToken;

    public GameSettingsInstanceListViewModel(
        IGameInstanceService instanceService,
        IGameVersionService gameVersionService,
        ILogger<GameSettingsInstanceListViewModel>? logger = null)
    {
        this.instanceService = instanceService;
        this.gameVersionService = gameVersionService;
        this.logger = logger ?? NullLogger<GameSettingsInstanceListViewModel>.Instance;

        Categories.Add(new GameSettingsInstanceCategory("all", Strings.GameSettings_AllCategory, string.Empty, "general/general_all_application"));
        Categories.Add(new GameSettingsInstanceCategory("mod_loader", Strings.GameSettings_ModLoaderCategory, string.Empty, "general/general_extention"));
        Categories.Add(new GameSettingsInstanceCategory("release", Strings.Download_ReleaseCategory, string.Empty, "instance_download_page/release"));
        Categories.Add(new GameSettingsInstanceCategory("snapshot", Strings.Download_SnapshotCategory, string.Empty, "instance_download_page/snapshot"));
        Categories.Add(new GameSettingsInstanceCategory("old_beta", Strings.Download_BetaCategory, "\u03b2"));
        Categories.Add(new GameSettingsInstanceCategory("old_alpha", Strings.Download_AlphaCategory, "\u03b1"));
        SelectCategory(Categories[0], refreshVisibleInstances: false);
    }

    public ObservableCollection<GameSettingsInstanceCategory> Categories { get; } = [];

    public List<GameSettingsInstanceItem> AllInstances { get; } = [];

    public ObservableCollection<GameSettingsInstanceItem> VisibleInstances { get; } = [];

    public bool HasVisibleInstances => VisibleInstances.Count > 0;

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

    public bool HasEmptyMessage => !string.IsNullOrWhiteSpace(EmptyMessage);

    public async Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (hasLoadedInstances || IsLoading)
            return;

        await RefreshCoreAsync(true, true, true, cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(true, true, true, cancellationToken);

    public Task RefreshForActivationAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(!hasLoadedInstances, !hasLoadedInstances, true, cancellationToken);

    public Task RefreshSilentlyAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(false, false, false, cancellationToken);

    public void SetPreserveFilteredSelection(bool value)
    {
        preserveFilteredSelection = value;
        RefreshVisibleInstances();
    }

    public void SelectCategory(GameSettingsInstanceCategory category, bool refreshVisibleInstances = true)
    {
        var changed = !ReferenceEquals(SelectedCategory, category)
            && !string.Equals(SelectedCategory?.Id, category.Id, StringComparison.OrdinalIgnoreCase);
        SelectedCategory = category;
        foreach (var item in Categories)
            item.IsSelected = ReferenceEquals(item, category);
        if (refreshVisibleInstances)
            RefreshVisibleInstances();
        if (hasLoadedInstances && changed)
            EntranceAnimationToken++;
    }

    public GameSettingsInstanceItem SelectInstance(GameSettingsInstanceItem instance)
    {
        SelectInstanceCore(instance);
        return instance;
    }

    public GameSettingsInstanceItem GetOrAdd(GameInstance instance)
    {
        var item = Find(instance.Id);
        if (item is not null)
            return item;
        item = CreateItem(instance);
        AllInstances.Add(item);
        RefreshVisibleInstances();
        return item;
    }

    public GameSettingsInstanceItem? Find(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return null;
        return AllInstances.FirstOrDefault(item =>
            string.Equals(item.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase));
    }

    public void AddOrUpdate(GameInstance instance)
    {
        if (!hasLoadedInstances)
            return;
        var item = Find(instance.Id);
        if (item is null)
        {
            AllInstances.Add(CreateItem(instance));
        }
        else
        {
            var wasSelected = ReferenceEquals(SelectedInstance, item);
            item.Update(instance, ResolveVersionType(instance));
            if (wasSelected)
                SelectInstanceCore(item, forceNotification: true);
        }
        RefreshVisibleInstances();
    }

    public bool Remove(string instanceId)
    {
        var removed = AllInstances.RemoveAll(item =>
            string.Equals(item.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            return false;
        if (string.Equals(SelectedInstance?.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase))
            SelectInstanceCore(null);
        RefreshVisibleInstances();
        return true;
    }

    partial void OnSearchQueryChanged(string value) => RefreshVisibleInstances();

    partial void OnLoadErrorChanged(string value) => OnPropertyChanged(nameof(HasLoadError));

    partial void OnEmptyMessageChanged(string value) => OnPropertyChanged(nameof(HasEmptyMessage));

    private async Task RefreshCoreAsync(
        bool playEntranceAnimation,
        bool clearVisibleInstancesBeforeRefresh,
        bool logRefreshResult,
        CancellationToken cancellationToken)
    {
        if (IsLoading)
            return;
        IsLoading = true;
        LoadError = string.Empty;
        EmptyMessage = string.Empty;
        if (clearVisibleInstancesBeforeRefresh)
            ClearVisibleInstances();
        var selectedId = SelectedInstance?.Instance.Id;
        try
        {
            var instances = await instanceService.GetInstancesAsync(cancellationToken);
            versionTypesByName = await LoadVersionTypesAsync(cancellationToken);
            Reconcile(instances);
            RestoreSelection(selectedId);
            hasLoadedInstances = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load game settings instances.");
            LoadError = Strings.Status_LoadInstancesFailed;
            hasLoadedInstances = false;
        }
        finally
        {
            IsLoading = false;
            if (hasLoadedInstances && playEntranceAnimation)
                EntranceAnimationToken++;
            RefreshVisibleInstances();
            if (hasLoadedInstances && logRefreshResult)
            {
                logger.LogInformation(
                    "Game settings instances refreshed. Count={InstanceCount} VisibleCount={VisibleCount} SelectedInstanceId={SelectedInstanceId}",
                    AllInstances.Count,
                    VisibleInstances.Count,
                    SelectedInstance?.Instance.Id);
            }
        }
    }

    private void RefreshVisibleInstances()
    {
        var result = GameSettingsInstanceFilter.Apply(
            AllInstances,
            SelectedCategory,
            SearchQuery,
            SelectedInstance,
            hasLoadedInstances,
            IsLoading,
            HasLoadError);
        EmptyMessage = result.EmptyMessage;
        if (result.ShouldClearSelectedInstance
            && (!preserveFilteredSelection || SelectedInstance is null || !ContainsSelectedInstance()))
        {
            SelectInstanceCore(null);
        }
        ApplyVisibleInstances(result.Instances);
    }

    private bool ContainsSelectedInstance() => SelectedInstance is not null && AllInstances.Any(item =>
        ReferenceEquals(item, SelectedInstance)
        || (!string.IsNullOrWhiteSpace(item.Instance.Id)
            && string.Equals(item.Instance.Id, SelectedInstance.Instance.Id, StringComparison.OrdinalIgnoreCase)));

    private void Reconcile(IReadOnlyList<GameInstance> instances)
    {
        var existing = AllInstances
            .Where(item => !string.IsNullOrWhiteSpace(item.Instance.Id))
            .GroupBy(item => item.Instance.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var next = new List<GameSettingsInstanceItem>(instances.Count);
        foreach (var instance in instances)
        {
            if (!string.IsNullOrWhiteSpace(instance.Id) && existing.TryGetValue(instance.Id, out var item))
            {
                item.Update(instance, ResolveVersionType(instance));
                next.Add(item);
            }
            else
            {
                next.Add(CreateItem(instance));
            }
        }
        AllInstances.Clear();
        AllInstances.AddRange(next);
    }

    private void RestoreSelection(string? instanceId) =>
        SelectInstanceCore(string.IsNullOrWhiteSpace(instanceId) ? null : Find(instanceId), forceNotification: true);

    private void SelectInstanceCore(GameSettingsInstanceItem? instance, bool forceNotification = false)
    {
        var previous = SelectedInstance;
        SelectedInstance = instance;
        if (forceNotification && ReferenceEquals(previous, instance))
            OnPropertyChanged(nameof(SelectedInstance));
        foreach (var item in AllInstances)
            item.IsSelected = ReferenceEquals(item, instance);
    }

    private void ClearVisibleInstances()
    {
        if (VisibleInstances.Count == 0)
            return;
        VisibleInstances.Clear();
        NotifyVisibleInstancesChanged();
    }

    private void ApplyVisibleInstances(IReadOnlyList<GameSettingsInstanceItem> instances)
    {
        var changed = false;
        for (var index = VisibleInstances.Count - 1; index >= 0; index--)
        {
            if (instances.Any(item => ReferenceEquals(item, VisibleInstances[index])))
                continue;
            VisibleInstances.RemoveAt(index);
            changed = true;
        }
        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (index < VisibleInstances.Count && ReferenceEquals(VisibleInstances[index], instance))
                continue;
            var existingIndex = -1;
            for (var candidate = index + 1; candidate < VisibleInstances.Count; candidate++)
            {
                if (!ReferenceEquals(VisibleInstances[candidate], instance))
                    continue;
                existingIndex = candidate;
                break;
            }
            if (existingIndex >= 0)
                VisibleInstances.Move(existingIndex, index);
            else
                VisibleInstances.Insert(index, instance);
            changed = true;
        }
        if (changed)
            NotifyVisibleInstancesChanged();
    }

    private void NotifyVisibleInstancesChanged()
    {
        OnPropertyChanged(nameof(VisibleInstances));
        OnPropertyChanged(nameof(HasVisibleInstances));
        OnPropertyChanged(nameof(HasEmptyMessage));
    }

    private GameSettingsInstanceItem CreateItem(GameInstance instance) =>
        new(instance, ResolveVersionType(instance));

    private string ResolveVersionType(GameInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.VersionType))
            return instance.VersionType;
        var versionName = string.IsNullOrWhiteSpace(instance.MinecraftVersion)
            ? instance.VersionName
            : instance.MinecraftVersion;
        return !string.IsNullOrWhiteSpace(versionName) && versionTypesByName.TryGetValue(versionName, out var type)
            ? type
            : string.Empty;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadVersionTypesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var versions = await gameVersionService.GetVersionsAsync(cancellationToken: cancellationToken);
            return versions
                .GroupBy(version => version.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => GameSettingsInstanceItem.NormalizeVersionType(group.First().Type),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to load version types for game settings instance classification.");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
