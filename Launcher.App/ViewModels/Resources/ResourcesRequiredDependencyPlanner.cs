using Launcher.App.Resources;
using Launcher.App.ViewModels.Download;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Resources;

internal sealed class ResourcesRequiredDependencyPlanner
{
    private readonly IResourceCatalogService? resourceCatalogService;
    private readonly IModService? modService;
    private readonly ResourcesOnlineProjectPageOptions options;
    private readonly ILogger? logger;
    private readonly Action<string> reportStatus;
    private readonly int pageSize;

    public ResourcesRequiredDependencyPlanner(
        IResourceCatalogService? resourceCatalogService,
        IModService? modService,
        ResourcesOnlineProjectPageOptions options,
        ILogger? logger,
        Action<string> reportStatus,
        int pageSize)
    {
        this.resourceCatalogService = resourceCatalogService;
        this.modService = modService;
        this.options = options;
        this.logger = logger;
        this.reportStatus = reportStatus;
        this.pageSize = pageSize;
    }

    public async Task<RequiredDependencyInstallPlan> ResolveInstallPlanAsync(
        ResourcesModVersionItemViewModel item,
        GameInstance instance,
        string? projectId,
        Func<IReadOnlyList<ResourcesModDependencyRequirementItemViewModel>, Task<RequiredDependenciesDialogChoice>> requestDialogAsync,
        CancellationToken cancellationToken)
    {
        if (options.Kind is not ResourceProjectKind.Mod
            || item.Version.RequiredDependencies.Count == 0
            || modService is null)
        {
            return RequiredDependencyInstallPlan.Continue;
        }

        var installedDependencies = await LoadEnabledLocalModIdentifiersAsync(instance, cancellationToken)
            .ConfigureAwait(false);
        var candidates = new List<RequiredDependencyInstallCandidate>();
        var dialogItems = new List<ResourcesModDependencyRequirementItemViewModel>();
        foreach (var dependency in item.Version.RequiredDependencies)
        {
            var candidate = await TryResolveRequiredDependencyInstallCandidateAsync(dependency, instance, cancellationToken)
                .ConfigureAwait(false);
            var state = ResolveDependencyRequirementState(candidate, installedDependencies);
            candidates.Add(candidate);
            dialogItems.Add(new ResourcesModDependencyRequirementItemViewModel(
                dependency,
                candidate.MinimumVersion,
                candidate.InstallVersion,
                state,
                options.FallbackIconKey));
        }

        var missingDependencies = candidates
            .Where(candidate => ResolveDependencyRequirementState(candidate, installedDependencies) is not RequiredDependencyRequirementState.Installed)
            .ToList();

        if (missingDependencies.Count == 0)
        {
            logger?.LogInformation(
                "Resource project required dependencies are already installed. Kind={Kind}, ProjectId={ProjectId}, VersionId={VersionId}, RequiredCount={RequiredCount}, InstanceId={InstanceId}",
                options.Kind,
                projectId,
                item.Version.VersionId,
                dialogItems.Count,
                instance.Id);
            return RequiredDependencyInstallPlan.Continue;
        }

        logger?.LogInformation(
            "Resource project required dependencies are missing. Kind={Kind}, ProjectId={ProjectId}, VersionId={VersionId}, MissingCount={MissingCount}, InstanceId={InstanceId}",
            options.Kind,
            projectId,
            item.Version.VersionId,
            missingDependencies.Count,
            instance.Id);

        var choice = await requestDialogAsync(dialogItems).ConfigureAwait(false);
        return new RequiredDependencyInstallPlan(choice, missingDependencies);
    }

    public async Task InstallRequiredDependenciesAsync(
        IReadOnlyList<RequiredDependencyInstallCandidate> missingDependencies,
        GameInstance instance,
        string? projectId,
        DownloadTaskItem? downloadTask,
        CancellationToken cancellationToken)
    {
        if (missingDependencies.Count == 0)
            return;

        var installedDependencies = await LoadEnabledLocalModIdentifiersAsync(instance, cancellationToken)
            .ConfigureAwait(false);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dependency in missingDependencies)
        {
            await InstallRequiredDependencyAsync(
                    dependency,
                    instance,
                    projectId,
                    installedDependencies,
                    visiting,
                    visited,
                    downloadTask,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    internal static RequiredDependencyRequirementState ResolveDependencyRequirementState(
        RequiredDependencyInstallCandidate candidate,
        InstalledDependencyCatalog installedDependencies)
    {
        var minimumVersion = candidate.MinimumVersion;
        if (minimumVersion is null || string.IsNullOrWhiteSpace(minimumVersion.VersionNumber))
            return RequiredDependencyRequirementState.Missing;

        var hasInstalledDependency = false;
        foreach (var identifier in EnumerateDependencyIdentifiers(candidate.Dependency.Project))
        {
            foreach (var installedVersion in installedDependencies.GetVersions(identifier))
            {
                hasInstalledDependency = true;
                if (ResourceDependencyVersionComparer.IsGreaterThanOrEqual(
                    installedVersion,
                    minimumVersion.VersionNumber))
                {
                    return RequiredDependencyRequirementState.Installed;
                }
            }
        }

        return hasInstalledDependency
            ? RequiredDependencyRequirementState.UpdateRequired
            : RequiredDependencyRequirementState.Missing;
    }

    internal static ResourceProjectVersion? SelectRequiredDependencyVersion(
        IReadOnlyList<ResourceProjectVersion> versions)
    {
        return versions.FirstOrDefault(version => string.Equals(
                   version.VersionType,
                   "release",
                   StringComparison.OrdinalIgnoreCase))
               ?? versions.FirstOrDefault();
    }

    internal static ResourceProjectVersion? ResolveRequiredDependencyMinimumVersion(
        ResourceProjectDependency dependency,
        IReadOnlyList<ResourceProjectVersion> versions)
    {
        if (string.IsNullOrWhiteSpace(dependency.VersionId))
            return null;

        return versions.FirstOrDefault(version => string.Equals(
            version.VersionId,
            dependency.VersionId,
            StringComparison.OrdinalIgnoreCase));
    }

    private async Task InstallRequiredDependencyAsync(
        RequiredDependencyInstallCandidate candidate,
        GameInstance instance,
        string? projectId,
        InstalledDependencyCatalog installedDependencies,
        ISet<string> visiting,
        ISet<string> visited,
        DownloadTaskItem? downloadTask,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dependency = candidate.Dependency;
        var project = dependency.Project;
        var dependencyKey = ResolveDependencyKey(project);
        if (string.IsNullOrWhiteSpace(dependencyKey)
            || visited.Contains(dependencyKey)
            || ResolveDependencyRequirementState(candidate, installedDependencies) is RequiredDependencyRequirementState.Installed)
        {
            return;
        }

        if (!visiting.Add(dependencyKey))
            return;

        try
        {
            var version = candidate.InstallVersion
                ?? throw new RequiredDependencyInstallException(project);
            foreach (var childDependency in version.RequiredDependencies)
            {
                var childCandidate = await ResolveRequiredDependencyInstallCandidateAsync(childDependency, instance, cancellationToken)
                    .ConfigureAwait(false);
                await InstallRequiredDependencyAsync(
                        childCandidate,
                        instance,
                        projectId,
                        installedDependencies,
                        visiting,
                        visited,
                        downloadTask,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (ResolveDependencyRequirementState(candidate, installedDependencies) is RequiredDependencyRequirementState.Installed)
                return;

            var installingMessage = string.Format(
                Strings.Status_ModRequiredDependencyInstallingFormat,
                project.Title);
            reportStatus(installingMessage);
            downloadTask?.Report(new LauncherProgress(ModProgressStages.DownloadingFile, installingMessage));
            await resourceCatalogService!.InstallProjectVersionAsync(version, instance, cancellationToken)
                .ConfigureAwait(false);
            AddDependencyIdentifiers(installedDependencies, dependency, version);
            logger?.LogInformation(
                "Resource project required dependency installed. ProjectId={ProjectId}, DependencyProjectId={DependencyProjectId}, VersionId={VersionId}, InstanceId={InstanceId}",
                projectId,
                project.ProjectId,
                version.VersionId,
                instance.Id);
        }
        finally
        {
            visiting.Remove(dependencyKey);
            visited.Add(dependencyKey);
        }
    }

    private async Task<RequiredDependencyInstallCandidate> TryResolveRequiredDependencyInstallCandidateAsync(
        ResourceProjectDependency dependency,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ResolveRequiredDependencyInstallCandidateAsync(dependency, instance, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequiredDependencyInstallException)
        {
            return new RequiredDependencyInstallCandidate(dependency, MinimumVersion: null, InstallVersion: null);
        }
    }

    private async Task<RequiredDependencyInstallCandidate> ResolveRequiredDependencyInstallCandidateAsync(
        ResourceProjectDependency dependency,
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        var project = dependency.Project;
        if (resourceCatalogService is null
            || project.Source is not (ResourceProjectSource.Modrinth or ResourceProjectSource.CurseForge)
            || project.Kind is not ResourceProjectKind.Mod)
        {
            throw new RequiredDependencyInstallException(project);
        }

        var result = await resourceCatalogService.GetProjectVersionsAsync(
                new ResourceProjectVersionsRequest
                {
                    Kind = ResourceProjectKind.Mod,
                    Source = project.Source,
                    ProjectId = project.ProjectId,
                    Slug = project.Slug,
                    MinecraftVersion = instance.MinecraftVersion,
                    Loader = instance.Loader,
                    IncludeAllVersions = false,
                    Offset = 0,
                    PageSize = pageSize
                },
                cancellationToken)
            .ConfigureAwait(false);
        var installVersion = SelectRequiredDependencyVersion(result.Versions)
            ?? throw new RequiredDependencyInstallException(project);
        var minimumVersion = ResolveRequiredDependencyMinimumVersion(dependency, result.Versions) ?? installVersion;
        return new RequiredDependencyInstallCandidate(dependency, minimumVersion, installVersion);
    }

    private async Task<InstalledDependencyCatalog> LoadEnabledLocalModIdentifiersAsync(
        GameInstance instance,
        CancellationToken cancellationToken)
    {
        if (modService is null)
            return new InstalledDependencyCatalog([]);

        var mods = await modService.GetModsAsync(instance, cancellationToken).ConfigureAwait(false);
        var installedVersionsByModId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods
            .Where(mod => mod.IsEnabled
                && !string.IsNullOrWhiteSpace(mod.ModId)
                && !string.IsNullOrWhiteSpace(mod.Version)))
        {
            var modId = mod.ModId!.Trim();
            var version = mod.Version!.Trim();
            if (!installedVersionsByModId.TryGetValue(modId, out var versions))
            {
                versions = [];
                installedVersionsByModId[modId] = versions;
            }

            versions.Add(version);
        }

        return new InstalledDependencyCatalog(installedVersionsByModId);
    }

    private static void AddDependencyIdentifiers(
        InstalledDependencyCatalog installedDependencies,
        ResourceProjectDependency dependency,
        ResourceProjectVersion version)
    {
        if (string.IsNullOrWhiteSpace(version.VersionNumber))
            return;

        foreach (var identifier in EnumerateDependencyIdentifiers(dependency.Project))
            installedDependencies.Add(identifier, version.VersionNumber);
    }

    private static IEnumerable<string> EnumerateDependencyIdentifiers(ResourceProject dependency)
    {
        if (!string.IsNullOrWhiteSpace(dependency.Slug))
            yield return dependency.Slug;
        if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
            yield return dependency.ProjectId;
    }

    private static string ResolveDependencyKey(ResourceProject dependency)
    {
        return string.IsNullOrWhiteSpace(dependency.ProjectId)
            ? dependency.Slug
            : dependency.ProjectId;
    }
}

internal sealed record RequiredDependencyInstallPlan(
    RequiredDependenciesDialogChoice Choice,
    IReadOnlyList<RequiredDependencyInstallCandidate> MissingDependencies)
{
    public static RequiredDependencyInstallPlan Continue { get; } =
        new(RequiredDependenciesDialogChoice.ContinueWithoutDependencies, []);
}

internal sealed record RequiredDependencyInstallCandidate(
    ResourceProjectDependency Dependency,
    ResourceProjectVersion? MinimumVersion,
    ResourceProjectVersion? InstallVersion);

internal sealed class InstalledDependencyCatalog
{
    private readonly Dictionary<string, List<string>> versionsByModId;

    public InstalledDependencyCatalog(Dictionary<string, List<string>> versionsByModId)
    {
        this.versionsByModId = versionsByModId;
    }

    public IEnumerable<string> GetVersions(string modId)
    {
        if (string.IsNullOrWhiteSpace(modId))
            return [];

        return versionsByModId.TryGetValue(modId.Trim(), out var versions)
            ? versions
            : [];
    }

    public void Add(string modId, string version)
    {
        if (string.IsNullOrWhiteSpace(modId) || string.IsNullOrWhiteSpace(version))
            return;

        var key = modId.Trim();
        if (!versionsByModId.TryGetValue(key, out var versions))
        {
            versions = [];
            versionsByModId[key] = versions;
        }

        versions.Add(version.Trim());
    }
}

internal enum RequiredDependenciesDialogChoice
{
    Cancel,
    ContinueWithoutDependencies,
    AutoInstallDependencies
}

internal sealed class RequiredDependencyInstallException : Exception
{
    public RequiredDependencyInstallException(ResourceProject dependency)
        : base($"Required dependency cannot be installed automatically: {dependency.ProjectId}")
    {
        DependencyProjectId = dependency.ProjectId;
        DependencyTitle = string.IsNullOrWhiteSpace(dependency.Title)
            ? dependency.Slug
            : dependency.Title;
    }

    public string DependencyProjectId { get; }

    public string DependencyTitle { get; }
}
