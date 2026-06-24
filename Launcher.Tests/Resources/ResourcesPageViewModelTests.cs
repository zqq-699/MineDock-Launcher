using Launcher.Application.Services;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Launcher.Tests.Fakes;

namespace Launcher.Tests.Resources;

public sealed class ResourcesPageViewModelTests
{
    [Fact]
    public void ResourcesPageShowsExpectedSectionsAndSelectsModByDefault()
    {
        var viewModel = new ResourcesPageViewModel();

        Assert.Equal(
            [
                Strings.Resources_SectionMods,
                Strings.Resources_SectionResourcePacks,
                Strings.Resources_SectionShaderPacks,
                Strings.Resources_SectionWorlds,
                Strings.Resources_SectionModpacks
            ],
            viewModel.Sections.Select(section => section.Title));
        Assert.Same(viewModel.Sections[0], viewModel.SelectedSection);
        Assert.True(viewModel.Sections[0].IsSelected);
        Assert.All(viewModel.Sections.Skip(1), section => Assert.False(section.IsSelected));
        Assert.True(viewModel.IsModsSection);
        Assert.Same(viewModel.ModPage, viewModel.CurrentSectionViewModel);
        Assert.Equal(string.Empty, viewModel.ModPage.SearchQuery);
    }

    [Fact]
    public void SelectSectionCommandUpdatesSelectionState()
    {
        var viewModel = new ResourcesPageViewModel();
        var targetSection = viewModel.Sections.Single(section => section.Id == "worlds");

        viewModel.SelectSectionCommand.Execute(targetSection);

        Assert.Same(targetSection, viewModel.SelectedSection);
        Assert.False(viewModel.Sections[0].IsSelected);
        Assert.True(targetSection.IsSelected);
        Assert.Equal(targetSection.Title, viewModel.PageTitle);
        Assert.False(viewModel.IsModsSection);
        Assert.Same(viewModel.WorldsPage, viewModel.CurrentSectionViewModel);
    }

    [Fact]
    public void ModFiltersUseExpectedDefaults()
    {
        var viewModel = new ResourcesPageViewModel();

        Assert.Equal([Strings.Resources_ModFilterAllVersions], viewModel.ModPage.VersionOptions.Select(option => option.Title));
        Assert.Equal(
            [
                Strings.Resources_ModFilterAllLoaders,
                Strings.Download_FabricLoaderTitle,
                Strings.Download_ForgeLoaderTitle,
                Strings.Download_NeoForgeLoaderTitle,
                Strings.Download_QuiltLoaderTitle
            ],
            viewModel.ModPage.LoaderOptions.Select(option => option.Title));
        Assert.Equal(
            [
                Strings.Resources_ModFilterAllSources,
                Strings.Resources_ModSourceModrinth,
                Strings.Resources_ModSourceCurseForge
            ],
            viewModel.ModPage.SourceOptions.Select(option => option.Title));
        Assert.Equal([Strings.Resources_ModFilterAllTypes], viewModel.ModPage.TypeOptions.Select(option => option.Title));
        Assert.Same(viewModel.ModPage.VersionOptions[0], viewModel.ModPage.SelectedVersionOption);
        Assert.Same(viewModel.ModPage.LoaderOptions[0], viewModel.ModPage.SelectedLoaderOption);
        Assert.Same(viewModel.ModPage.SourceOptions[0], viewModel.ModPage.SelectedSourceOption);
        Assert.Same(viewModel.ModPage.TypeOptions[0], viewModel.ModPage.SelectedTypeOption);
    }

    [Fact]
    public void ModFilterSelectionCanBeChanged()
    {
        var viewModel = new ResourcesPageViewModel();
        var versionOption = new ResourcesFilterOptionItem { Id = "1.21", Title = "1.21" };
        var loaderOption = new ResourcesFilterOptionItem { Id = "fabric", Title = "Fabric" };
        var sourceOption = new ResourcesFilterOptionItem { Id = "modrinth", Title = "Modrinth" };
        var typeOption = new ResourcesFilterOptionItem { Id = "library", Title = "Library" };

        viewModel.ModPage.VersionOptions.Add(versionOption);
        viewModel.ModPage.LoaderOptions.Add(loaderOption);
        viewModel.ModPage.SourceOptions.Add(sourceOption);
        viewModel.ModPage.TypeOptions.Add(typeOption);

        viewModel.ModPage.SelectedVersionOption = versionOption;
        viewModel.ModPage.SelectedLoaderOption = loaderOption;
        viewModel.ModPage.SelectedSourceOption = sourceOption;
        viewModel.ModPage.SelectedTypeOption = typeOption;

        Assert.Same(versionOption, viewModel.ModPage.SelectedVersionOption);
        Assert.Same(loaderOption, viewModel.ModPage.SelectedLoaderOption);
        Assert.Same(sourceOption, viewModel.ModPage.SelectedSourceOption);
        Assert.Same(typeOption, viewModel.ModPage.SelectedTypeOption);
    }

    [Fact]
    public void SelectSectionCommandMapsAllSectionsToChildViewModels()
    {
        var viewModel = new ResourcesPageViewModel();

        var expectedMappings = new Dictionary<string, ResourcesSectionViewModelBase>
        {
            ["mods"] = viewModel.ModPage,
            ["resource_packs"] = viewModel.ResourcePacksPage,
            ["shader_packs"] = viewModel.ShaderPacksPage,
            ["worlds"] = viewModel.WorldsPage,
            ["modpacks"] = viewModel.ModpacksPage
        };

        foreach (var section in viewModel.Sections)
        {
            viewModel.SelectSectionCommand.Execute(section);

            Assert.Same(expectedMappings[section.Id], viewModel.CurrentSectionViewModel);
            Assert.Equal(section.Id == "mods", viewModel.IsModsSection);
        }
    }

    [Fact]
    public async Task ModPageRefreshProjectsLoadsVisibleProjects()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "sodium",
                    Slug = "sodium",
                    Title = "Sodium",
                    Description = "Rendering optimization",
                    SupportedMinecraftVersions = ["1.20.1"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 2000,
                    ProjectUrl = "https://modrinth.com/mod/sodium"
                }
            ]
        });
        var viewModel = new ResourcesPageViewModel(service);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.True(viewModel.ModPage.HasVisibleProjects);
        Assert.False(viewModel.ModPage.CanShowEmptyState);
        Assert.Equal("Sodium", viewModel.ModPage.VisibleProjects[0].Title);
        Assert.Equal($"1.20  fabric  {Strings.Resources_ModSourceModrinth}", viewModel.ModPage.VisibleProjects[0].Subtitle);
        Assert.Equal(string.Format(Strings.Resources_ModDownloadsFormat, "2,000"), viewModel.ModPage.VisibleProjects[0].TrailingText);
    }

    [Fact]
    public async Task ModPageRefreshProjectsUsesMinecraftReleaseOrderForVersionSummary()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "future-mod",
                    Slug = "future-mod",
                    Title = "Future Mod",
                    SupportedMinecraftVersions = ["26", "1.21.4", "1.20.1", "1.12.2"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 2000
                }
            ]
        });
        var versionService = new FakeGameVersionService(
        [
            new MinecraftVersionInfo("26", "release", false),
            new MinecraftVersionInfo("1.21.8", "release", false),
            new MinecraftVersionInfo("1.20.6", "release", false),
            new MinecraftVersionInfo("24w45a", "snapshot", false),
            new MinecraftVersionInfo("1.19.4", "release", false),
            new MinecraftVersionInfo("1.18.2", "release", false),
            new MinecraftVersionInfo("1.17.1", "release", false),
            new MinecraftVersionInfo("1.16.5", "release", false),
            new MinecraftVersionInfo("1.15.2", "release", false),
            new MinecraftVersionInfo("1.14.4", "release", false),
            new MinecraftVersionInfo("1.13.2", "release", false),
            new MinecraftVersionInfo("1.12.2", "release", false)
        ]);
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: versionService);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Equal($"1.20+, 1.12  fabric  {Strings.Resources_ModSourceModrinth}", viewModel.ModPage.VisibleProjects[0].Subtitle);
        Assert.Equal(1, versionService.CallCount);
    }

    [Fact]
    public async Task ModPageRefreshProjectsFallsBackWhenMinecraftReleaseOrderFails()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "fallback-mod",
                    Slug = "fallback-mod",
                    Title = "Fallback Mod",
                    SupportedMinecraftVersions = ["26", "1.21.4", "1.20.1", "1.12.2"],
                    SupportedLoaders = ["fabric"],
                    Downloads = 2000
                }
            ]
        });
        var viewModel = new ResourcesPageViewModel(service, gameVersionService: new ThrowingGameVersionService());

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.True(viewModel.ModPage.HasVisibleProjects);
        Assert.Equal($"26, 1.21, 1.20, 1.12  fabric  {Strings.Resources_ModSourceModrinth}", viewModel.ModPage.VisibleProjects[0].Subtitle);
    }

    [Fact]
    public void ModProjectItemSubtitleShowsVersionsLoadersAndSource()
    {
        var item = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            Title = "Test Mod",
            Description = "This should not be shown",
            SupportedMinecraftVersions = ["1.20.1", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.12.2"],
            SupportedLoaders = ["forge", "fabric"],
            Downloads = 1
        }, ["1.20", "1.19", "1.18", "1.17", "1.16", "1.15", "1.14", "1.13", "1.12"]);

        Assert.Equal($"1.16+, 1.12  fabric/forge  {Strings.Resources_ModSourceModrinth}", item.Subtitle);
        Assert.DoesNotContain("This should not be shown", item.Subtitle);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterUsesOfficialReleaseOrderForContinuity()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(
            ["26", "1.21.4", "1.20.1", "1.12.2"],
            ["26", "1.21.8", "1.20.6", "1.19.4", "1.18.2", "1.17.1", "1.16.5", "1.15.2", "1.14.4", "1.13.2", "1.12.2"]);

        Assert.Equal("1.20+, 1.12", summary);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterSupportsSingleNumberVersion()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(["26"], ["26", "1.21.8"]);

        Assert.Equal("26", summary);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterDoesNotHideVersionsWithoutOfficialOrder()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(["26", "1.21.4", "1.20.1", "1.12.2"]);

        Assert.Equal("26, 1.21, 1.20, 1.12", summary);
    }

    [Fact]
    public void MinecraftVersionSupportFormatterDeduplicatesPatchVersions()
    {
        var summary = ResourceMinecraftVersionSupportFormatter.Format(
            ["1.20.6", "1.20.1"],
            ["1.20.6", "1.20.1"]);

        Assert.Equal("1.20", summary);
    }

    [Fact]
    public void ModProjectItemSubtitleUsesFriendlyFallbacksWhenMetadataIsMissing()
    {
        var item = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.CurseForge,
            Title = "Unknown Mod",
            Downloads = 1
        });

        Assert.Equal(
            $"{Strings.Resources_ModVersionsUnknown}  {Strings.Resources_ModLoadersUnknown}  {Strings.Resources_ModSourceCurseForge}",
            item.Subtitle);
    }

    [Theory]
    [InlineData(9999, "9,999")]
    [InlineData(10000, "1万")]
    [InlineData(12345, "1.23万")]
    [InlineData(100000000, "1亿")]
    [InlineData(112820485, "1.13亿")]
    public void ModProjectItemFormatsDownloadsWithChineseCompactUnits(long downloads, string expectedValue)
    {
        var item = new ResourcesModProjectItemViewModel(new ResourceProject
        {
            Source = ResourceProjectSource.Modrinth,
            Title = "Test Mod",
            Downloads = downloads
        });

        Assert.Equal(string.Format(Strings.Resources_ModDownloadsFormat, expectedValue), item.TrailingText);
    }

    [Fact]
    public async Task BeginEnsureCurrentSectionLoadedStartsDefaultModPageLoadWithoutWaiting()
    {
        var dispatcher = new QueueingUiDispatcher();
        var pendingResult = new TaskCompletionSource<ResourceCatalogSearchResult>();
        var service = new FakeResourceCatalogService(pendingResult.Task);
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        viewModel.BeginEnsureCurrentSectionLoaded();

        Assert.Equal(0, service.CallCount);
        Assert.True(viewModel.ModPage.IsLoadingProjects);
        Assert.True(viewModel.ModPage.CanShowLoadingState);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => service.CallCount == 1);
        Assert.NotNull(service.LastRequest);

        pendingResult.SetResult(new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = "iris",
                    Slug = "iris",
                    Title = "Iris",
                    Description = "Shader support",
                    Downloads = 1000,
                    ProjectUrl = "https://modrinth.com/mod/iris"
                }
            ]
        });

        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() =>
            !viewModel.ModPage.IsLoadingProjects
            && viewModel.ModPage.VisibleProjects.Count == 1);
        Assert.Equal("Iris", viewModel.ModPage.VisibleProjects[0].Title);
    }

    [Fact]
    public async Task ModPageBackgroundLoadFailureShowsErrorAfterCompletion()
    {
        var dispatcher = new QueueingUiDispatcher();
        var pendingResult = new TaskCompletionSource<ResourceCatalogSearchResult>();
        var service = new FakeResourceCatalogService(pendingResult.Task);
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        viewModel.BeginEnsureCurrentSectionLoaded();

        Assert.True(viewModel.ModPage.IsLoadingProjects);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => service.CallCount == 1);
        pendingResult.SetException(new InvalidOperationException("network failed"));

        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() =>
            !viewModel.ModPage.IsLoadingProjects
            && viewModel.ModPage.HasLoadErrorMessage);
        Assert.Equal(Strings.Resources_ModProjectsLoadError, viewModel.ModPage.LoadErrorMessage);
    }

    [Fact]
    public async Task ModPageRefreshDoesNotLetCanceledOlderResultOverwriteLatestResult()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new ControlledResourceCatalogService();
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        var firstLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        var oldCall = await service.WaitForCallAsync(0);

        var secondLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();

        var newCall = await service.WaitForCallAsync(1);
        newCall.SetResult(CreateSingleProjectResult("new", "New Mod", 2000));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount >= 1);
        while (dispatcher.PendingCount > 0)
            dispatcher.RunNext();
        await TestAsync.WaitForAsync(() =>
            viewModel.ModPage.VisibleProjects.Count == 1
            && viewModel.ModPage.VisibleProjects[0].Title == "New Mod");

        oldCall.SetResult(CreateSingleProjectResult("old", "Old Mod", 100));
        await firstLoad;
        await secondLoad;

        Assert.Equal("New Mod", viewModel.ModPage.VisibleProjects.Single().Title);
    }

    [Fact]
    public async Task ModPageRefreshProjectsDoesNotShowCurseForgeMissingKeyWarning()
    {
        var service = new FakeResourceCatalogService(new ResourceCatalogSearchResult
        {
            Projects = [],
            IsCurseForgeUnavailable = true,
            IsCurseForgeApiKeyMissing = true
        });
        var viewModel = new ResourcesPageViewModel(service);

        await viewModel.ModPage.RefreshProjectsCommand.ExecuteAsync(null);

        Assert.Equal(string.Empty, viewModel.ModPage.PartialWarningMessage);
        Assert.False(viewModel.ModPage.HasPartialWarningMessage);
    }

    [Fact]
    public async Task ModPageLoadRendersProjectsInSmallBatches()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new FakeResourceCatalogService(CreateProjectResult(25));
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        var loadTask = viewModel.ModPage.RefreshProjectsAsync();

        Assert.True(viewModel.ModPage.IsLoadingProjects);
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);

        dispatcher.RunNext();
        Assert.False(viewModel.ModPage.IsLoadingProjects);
        Assert.Equal(12, viewModel.ModPage.VisibleProjects.Count);
        Assert.Equal(1, viewModel.ModPage.ListEntranceAnimationToken);
        Assert.False(loadTask.IsCompleted);

        dispatcher.RunNext();
        Assert.Equal(20, viewModel.ModPage.VisibleProjects.Count);
        Assert.Equal(1, viewModel.ModPage.ListEntranceAnimationToken);
        Assert.False(loadTask.IsCompleted);

        dispatcher.RunNext();
        await loadTask;
        Assert.Equal(25, viewModel.ModPage.VisibleProjects.Count);
        Assert.Equal(1, viewModel.ModPage.ListEntranceAnimationToken);
    }

    [Fact]
    public async Task ModPageLoadRaisesEntranceAnimationTokenBeforeInitialProjectBatch()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new FakeResourceCatalogService(CreateProjectResult(3));
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);
        var events = new List<string>();
        viewModel.ModPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ResourcesModPageViewModel.ListEntranceAnimationToken))
                events.Add("token");
        };
        viewModel.ModPage.VisibleProjects.CollectionChanged += (_, args) =>
        {
            if (args.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                events.Add("add");
        };

        var loadTask = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        await loadTask;

        Assert.Equal("token", events.First());
        Assert.Contains("add", events);
    }

    [Fact]
    public async Task ModPageCanceledBatchAppendDoesNotContinueAfterNewRefresh()
    {
        var dispatcher = new QueueingUiDispatcher();
        var service = new ControlledResourceCatalogService();
        var viewModel = new ResourcesPageViewModel(service, uiDispatcher: dispatcher);

        var oldLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        var oldCall = await service.WaitForCallAsync(0);
        oldCall.SetResult(CreateProjectResult(25, "old"));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount == 1);
        dispatcher.RunNext();
        Assert.Equal(12, viewModel.ModPage.VisibleProjects.Count);
        Assert.All(viewModel.ModPage.VisibleProjects, item => Assert.StartsWith("Old", item.Title));

        var newLoad = viewModel.ModPage.RefreshProjectsAsync();
        dispatcher.RunNext();
        if (service.CallCount < 2 && dispatcher.PendingCount > 0)
            dispatcher.RunNext();
        var newCall = await service.WaitForCallAsync(1);
        newCall.SetResult(CreateProjectResult(1, "new"));
        await TestAsync.WaitForAsync(() => dispatcher.PendingCount >= 1);

        while (dispatcher.PendingCount > 0)
            dispatcher.RunNext();

        await oldLoad;
        await newLoad;
        Assert.Single(viewModel.ModPage.VisibleProjects);
        Assert.Equal("New 0", viewModel.ModPage.VisibleProjects[0].Title);
    }

    private sealed class FakeResourceCatalogService : IResourceCatalogService
    {
        private readonly Task<ResourceCatalogSearchResult> resultTask;

        public FakeResourceCatalogService(ResourceCatalogSearchResult result)
            : this(Task.FromResult(result))
        {
        }

        public FakeResourceCatalogService(Task<ResourceCatalogSearchResult> resultTask)
        {
            this.resultTask = resultTask;
        }

        public int CallCount { get; private set; }

        public ResourceCatalogSearchRequest? LastRequest { get; private set; }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return resultTask;
        }
    }

    private sealed class ControlledResourceCatalogService : IResourceCatalogService
    {
        private readonly object gate = new();
        private readonly List<TaskCompletionSource<ResourceCatalogSearchResult>> calls = [];

        public int CallCount
        {
            get
            {
                lock (gate)
                    return calls.Count;
            }
        }

        public Task<ResourceCatalogSearchResult> SearchModsAsync(
            ResourceCatalogSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                var result = new TaskCompletionSource<ResourceCatalogSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                calls.Add(result);
                return result.Task;
            }
        }

        public async Task<TaskCompletionSource<ResourceCatalogSearchResult>> WaitForCallAsync(int index)
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
            while (DateTimeOffset.UtcNow < deadline)
            {
                lock (gate)
                {
                    if (calls.Count > index)
                        return calls[index];
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Resource catalog call {index} was not observed.");
        }
    }

    private sealed class ThrowingGameVersionService : IGameVersionService
    {
        public Task<IReadOnlyList<MinecraftVersionInfo>> GetVersionsAsync(
            DownloadSourcePreference downloadSourcePreference = DownloadSourcePreference.Auto,
            CancellationToken cancellationToken = default,
            int downloadSpeedLimitMbPerSecond = 0)
        {
            return Task.FromException<IReadOnlyList<MinecraftVersionInfo>>(
                new InvalidOperationException("version order failed"));
        }
    }

    private sealed class QueueingUiDispatcher : IUiDispatcher
    {
        private readonly object gate = new();
        private readonly Queue<Action> actions = new();

        public bool HasAccess => true;

        public int PendingCount
        {
            get
            {
                lock (gate)
                    return actions.Count;
            }
        }

        public void Post(Action action)
        {
            lock (gate)
                actions.Enqueue(action);
        }

        public void Invoke(Action action)
        {
            action();
        }

        public void RunNext()
        {
            Action action;
            lock (gate)
                action = actions.Dequeue();
            action.Invoke();
        }
    }

    private static ResourceCatalogSearchResult CreateSingleProjectResult(string slug, string title, long downloads)
    {
        return new ResourceCatalogSearchResult
        {
            Projects =
            [
                new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = slug,
                    Slug = slug,
                    Title = title,
                    Description = title,
                    Downloads = downloads,
                    ProjectUrl = $"https://modrinth.com/mod/{slug}"
                }
            ]
        };
    }

    private static ResourceCatalogSearchResult CreateProjectResult(int count, string prefix = "project")
    {
        return new ResourceCatalogSearchResult
        {
            Projects = Enumerable.Range(0, count)
                .Select(index => new ResourceProject
                {
                    Source = ResourceProjectSource.Modrinth,
                    ProjectId = $"{prefix}-{index}",
                    Slug = $"{prefix}-{index}",
                    Title = $"{char.ToUpperInvariant(prefix[0])}{prefix[1..]} {index}",
                    Description = $"{prefix} {index}",
                    Downloads = count - index,
                    ProjectUrl = $"https://modrinth.com/mod/{prefix}-{index}"
                })
                .ToList()
        };
    }
}
