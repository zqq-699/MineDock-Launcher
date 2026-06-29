using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesWorldsPageViewModel : ResourcesModPageViewModel
{
    public ResourcesWorldsPageViewModel(
        ResourcesPageViewModel parent,
        IResourceCatalogService? resourceCatalogService = null,
        ILogger? logger = null,
        IUiDispatcher? uiDispatcher = null,
        IGameVersionService? gameVersionService = null,
        IGameInstanceService? gameInstanceService = null,
        IStatusService? statusService = null,
        IFilePickerService? filePickerService = null,
        IFloatingMessageService? floatingMessageService = null,
        DownloadTasksPageViewModel? downloadTasksPage = null)
        : base(
            parent,
            CreateWorldOptions(),
            resourceCatalogService,
            logger,
            uiDispatcher,
            gameVersionService,
            gameInstanceService,
            statusService,
            filePickerService,
            floatingMessageService,
            downloadTasksPage)
    {
    }

    private static ResourcesOnlineProjectPageOptions CreateWorldOptions()
    {
        return new ResourcesOnlineProjectPageOptions(
            ResourceProjectKind.World,
            Strings.Resources_SectionWorlds,
            "instance_setting_page/saves",
            ShowsLoaderFilters: false,
            Strings.Resources_ModFilterAllVersions,
            Strings.Resources_WorldFilterAllLoaders,
            Strings.Resources_WorldProjectsLoading,
            Strings.Resources_WorldProjectsEmpty,
            Strings.Resources_WorldProjectsLoadError,
            Strings.Resources_WorldProjectsLoadingMore,
            Strings.Resources_WorldProjectsNoMore,
            Strings.Resources_WorldProjectsLoadMoreError,
            Strings.Resources_WorldCurseForgeMissingApiKey,
            Strings.Resources_WorldDetailsInfoSection,
            Strings.Resources_WorldInstallTargetSection,
            Strings.Resources_WorldInstallTargetLocal,
            Strings.Resources_WorldInstallTargetsLoading,
            Strings.Resources_WorldInstallTargetsLoadError,
            Strings.Resources_WorldVersionsLoading,
            Strings.Resources_WorldVersionsEmpty,
            Strings.Resources_WorldVersionsEmptyLocal,
            Strings.Resources_WorldVersionsFilterEmpty,
            Strings.Resources_WorldVersionsLoadError,
            Strings.Resources_WorldVersionsLoadingMore,
            Strings.Resources_WorldVersionsNoMore,
            Strings.Resources_WorldVersionsLoadMoreError,
            Strings.Resources_WorldVersionsAllTitle,
            Strings.FilePicker_WorldDownloadDirectoryTitle,
            Strings.Status_WorldDownloading,
            Strings.Status_WorldDownloadingFormat,
            Strings.Status_WorldDownloadedFormat,
            Strings.Status_WorldDownloadFailed,
            Strings.Status_WorldInstalledFormat,
            Strings.Status_WorldInstallFailed,
            Strings.Resources_WorldDownloadFileExistsMessageFormat,
            [
                new("adventure", Strings.Resources_WorldFilterTypeAdventure, ResourceProjectCategory.Adventure),
                new("creation", Strings.Resources_WorldFilterTypeCreation, ResourceProjectCategory.Creation),
                new("game-map", Strings.Resources_WorldFilterTypeGameMap, ResourceProjectCategory.GameMap),
                new("parkour", Strings.Resources_WorldFilterTypeParkour, ResourceProjectCategory.Parkour),
                new("puzzle", Strings.Resources_WorldFilterTypePuzzle, ResourceProjectCategory.Puzzle),
                new("survival", Strings.Resources_WorldFilterTypeSurvival, ResourceProjectCategory.Survival)
            ],
            [
                new ResourcesFilterOptionItem { Id = "curseforge", Title = Strings.Resources_ModSourceCurseForge }
            ]);
    }
}
