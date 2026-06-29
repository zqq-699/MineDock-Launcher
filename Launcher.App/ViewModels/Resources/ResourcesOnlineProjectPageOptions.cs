using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

public sealed record ResourcesOnlineProjectTypeOption(
    string Id,
    string Title,
    ResourceProjectCategory Category);

public sealed record ResourcesOnlineProjectPageOptions(
    ResourceProjectKind Kind,
    string Title,
    string FallbackIconKey,
    bool ShowsLoaderFilters,
    string AllVersionsText,
    string AllLoadersText,
    string ProjectsLoadingText,
    string ProjectsEmptyText,
    string ProjectsLoadErrorText,
    string ProjectsLoadingMoreText,
    string ProjectsNoMoreText,
    string ProjectsLoadMoreErrorText,
    string CurseForgeMissingApiKeyText,
    string DetailsInfoSectionText,
    string InstallTargetSectionText,
    string InstallTargetLocalText,
    string InstallTargetsLoadingText,
    string InstallTargetsLoadErrorText,
    string VersionsLoadingText,
    string VersionsEmptyText,
    string VersionsEmptyLocalText,
    string VersionsFilterEmptyText,
    string VersionsLoadErrorText,
    string VersionsLoadingMoreText,
    string VersionsNoMoreText,
    string VersionsLoadMoreErrorText,
    string VersionsAllTitleText,
    string DownloadDirectoryPickerTitle,
    string DownloadingText,
    string DownloadingFormat,
    string DownloadedFormat,
    string DownloadFailedText,
    string InstalledFormat,
    string InstallFailedText,
    string FileExistsMessageFormat,
    IReadOnlyList<ResourcesOnlineProjectTypeOption> TypeOptions,
    IReadOnlyList<ResourcesFilterOptionItem>? SourceOptions = null,
    ResourcesOnlineProjectInstallTargetMode InstallTargetMode = ResourcesOnlineProjectInstallTargetMode.ExistingInstance,
    string? InstallTargetNewInstanceText = null);
