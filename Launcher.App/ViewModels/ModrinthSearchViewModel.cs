using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class ModrinthSearchViewModel : ObservableObject
{
    private readonly IModrinthService modrinthService;
    private readonly IStatusService statusService;

    [ObservableProperty]
    private string modSearchQuery = string.Empty;

    [ObservableProperty]
    private ModrinthProject? selectedModrinthProject;

    public ModrinthSearchViewModel(
        IModrinthService modrinthService,
        IStatusService statusService)
    {
        this.modrinthService = modrinthService;
        this.statusService = statusService;
    }

    public ObservableCollection<ModrinthProject> ModrinthProjects { get; } = [];

    public async Task SearchModsAsync(GameInstance? selectedInstance)
    {
        if (selectedInstance is null)
        {
            ReportStatus(Strings.Status_SelectInstanceFirst);
            return;
        }

        ReportStatus(Strings.Status_SearchingModrinth);
        var projects = await modrinthService.SearchModsAsync(
            ModSearchQuery,
            selectedInstance.MinecraftVersion,
            selectedInstance.Loader);
        ModrinthProjects.ReplaceWith(projects);

        ReportStatus(string.Format(Strings.Status_ModrinthResultsFoundFormat, ModrinthProjects.Count));
    }

    public async Task<bool> InstallSelectedModAsync(
        GameInstance? selectedInstance,
        IProgress<LauncherProgress>? progress)
    {
        if (selectedInstance is null || SelectedModrinthProject is null)
            return false;

        await modrinthService.InstallLatestCompatibleAsync(SelectedModrinthProject, selectedInstance, progress);
        ReportStatus(string.Format(Strings.Status_ModInstalledFormat, SelectedModrinthProject.Title));
        return true;
    }

    private void ReportStatus(string message)
    {
        statusService.Report(message);
    }
}
