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
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed partial class ResourcesProjectDetailsViewModel : ObservableObject, IDisposable
{
    private readonly Stack<ResourcesModProjectItemViewModel> backStack = new();
    private readonly ResourcesOnlineProjectPageOptions options;
    private readonly IResourceCatalogService? resourceCatalogService;
    private readonly IUiDispatcher uiDispatcher;
    private readonly ILogger? logger;
    private CancellationTokenSource? dependenciesCancellation;

    internal ResourcesProjectDetailsViewModel(
        ResourcesOnlineProjectPageOptions options,
        IResourceCatalogService? resourceCatalogService,
        IUiDispatcher uiDispatcher,
        ILogger? logger)
    {
        this.options = options;
        this.resourceCatalogService = resourceCatalogService;
        this.uiDispatcher = uiDispatcher;
        this.logger = logger;
    }

    public event Action<ResourcesModProjectItemViewModel>? ProjectChanged;

    [ObservableProperty]
    private ResourcesModProjectItemViewModel? currentProject;

    [ObservableProperty]
    private bool isLoadingDependencies;

    public ObservableCollection<ResourcesModProjectItemViewModel> RequiredDependencies { get; } = [];

    public bool CanGoBackToDependencyParent => backStack.Count > 0;

    public bool HasRequiredDependencies => RequiredDependencies.Count > 0;

    public string InfoSectionText => options.DetailsInfoSectionText;

    public void SelectRoot(ResourcesModProjectItemViewModel project)
    {
        backStack.Clear();
        SelectCore(project);
    }

    [RelayCommand]
    public void OpenDependency(ResourcesModProjectItemViewModel? project)
    {
        if (project is null)
            return;

        if (CurrentProject is not null)
            backStack.Push(CurrentProject);
        SelectCore(project);
        logger?.LogInformation(
            "Resource dependency project selected. Kind={Kind} Source={Source} ProjectId={ProjectId}",
            options.Kind,
            project.Project.Source,
            project.Project.ProjectId);
    }

    public bool TryGoBack(out ResourcesModProjectItemViewModel? project)
    {
        if (!backStack.TryPop(out project) || project is null)
            return false;

        SelectCore(project);
        return true;
    }

    public void Reset()
    {
        CancelDependenciesLoad();
        backStack.Clear();
        CurrentProject = null;
        RequiredDependencies.Clear();
        IsLoadingDependencies = false;
        NotifyStateChanged();
    }

    public void Dispose()
    {
        CancelDependenciesLoad();
    }

    private void SelectCore(ResourcesModProjectItemViewModel project)
    {
        CurrentProject = project;
        RequiredDependencies.Clear();
        NotifyStateChanged();
        ProjectChanged?.Invoke(project);
        _ = LoadDependenciesAsync(project);
    }

    private async Task LoadDependenciesAsync(ResourcesModProjectItemViewModel project)
    {
        var replacement = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref dependenciesCancellation, replacement);
        previous?.Cancel();
        previous?.Dispose();
        var cancellationToken = replacement.Token;

        if (resourceCatalogService is null
            || project.Project.Kind is not ResourceProjectKind.Mod
            || project.Project.Source is not (ResourceProjectSource.Modrinth or ResourceProjectSource.CurseForge))
        {
            IsLoadingDependencies = false;
            return;
        }

        IsLoadingDependencies = true;
        try
        {
            var result = await resourceCatalogService.GetProjectDependenciesAsync(
                new ResourceProjectDependenciesRequest
                {
                    Kind = project.Project.Kind,
                    Source = project.Project.Source,
                    ProjectId = project.Project.ProjectId,
                    Slug = project.Project.Slug
                },
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var items = result.RequiredProjects
                .Select(dependency => new ResourcesModProjectItemViewModel(dependency, fallbackIconKey: options.FallbackIconKey))
                .ToList();
            uiDispatcher.Invoke(() =>
            {
                if (cancellationToken.IsCancellationRequested || !ReferenceEquals(CurrentProject, project))
                    return;
                RequiredDependencies.Clear();
                foreach (var item in items)
                    RequiredDependencies.Add(item);
                IsLoadingDependencies = false;
                NotifyStateChanged();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested)
                return;
            uiDispatcher.Invoke(() =>
            {
                RequiredDependencies.Clear();
                IsLoadingDependencies = false;
                NotifyStateChanged();
            });
            logger?.LogError(
                exception,
                "Failed to load resource project dependencies. Kind={Kind} Source={Source} ProjectId={ProjectId}",
                options.Kind,
                project.Project.Source,
                project.Project.ProjectId);
        }
    }

    private void CancelDependenciesLoad()
    {
        var cancellation = Interlocked.Exchange(ref dependenciesCancellation, null);
        cancellation?.Cancel();
        cancellation?.Dispose();
    }

    private void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(CanGoBackToDependencyParent));
        OnPropertyChanged(nameof(HasRequiredDependencies));
    }
}
