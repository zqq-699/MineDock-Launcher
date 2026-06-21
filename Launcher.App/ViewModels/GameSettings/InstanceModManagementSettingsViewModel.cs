using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.ObjectModel;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class InstanceModManagementSettingsViewModel : GameSettingsDetailsSectionViewModelBase
{
    private readonly LocalModsViewModel localModsViewModel;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly ILogger<InstanceModManagementSettingsViewModel> logger;
    private GameInstance? selectedInstance;

    [ObservableProperty]
    private int installedModCount;

    [ObservableProperty]
    private int enabledModCount;

    [ObservableProperty]
    private ModManagementModItemViewModel? selectedMod;

    [ObservableProperty]
    private string modSearchQuery = string.Empty;

    public InstanceModManagementSettingsViewModel(
        GameSettingsDetailsViewModel parent,
        LocalModsViewModel localModsViewModel,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        ILogger<InstanceModManagementSettingsViewModel>? logger = null)
        : base(parent)
    {
        this.localModsViewModel = localModsViewModel;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.logger = logger ?? NullLogger<InstanceModManagementSettingsViewModel>.Instance;
        this.localModsViewModel.ModsChanged += LocalModsViewModel_ModsChanged;
    }

    public ObservableCollection<ModManagementModItemViewModel> Mods { get; } = [];

    public bool HasMods => Mods.Count > 0;

    public bool CanShowModEmptyState => !HasMods;

    public string InstalledSummaryText => string.Format(
        Strings.GameSettings_ModManagementInstalledSummaryFormat,
        InstalledModCount,
        EnabledModCount);

    public string ModEmptyMessage => string.IsNullOrWhiteSpace(ModSearchQuery)
        ? Strings.GameSettings_ModManagementEmptyMessage
        : Strings.GameSettings_ModManagementSearchEmptyMessage;

    public async Task SetSelectedInstanceAsync(GameInstance? instance)
    {
        selectedInstance = instance;
        try
        {
            await localModsViewModel.SetSelectedInstanceAsync(instance);
            RefreshFromLocalMods();
        }
        catch (Exception)
        {
            Mods.Clear();
            SelectedMod = null;
            RefreshSummary();
            OnPropertyChanged(nameof(HasMods));
            OnPropertyChanged(nameof(CanShowModEmptyState));
            statusService.Report(Strings.Status_LoadLocalModsFailed);
        }
    }

    [RelayCommand]
    private void OpenModFolder()
    {
        if (selectedInstance is null)
            return;

        try
        {
            var modsDirectory = instanceFolderService.EnsureDirectoryExists(
                Path.Combine(selectedInstance.InstanceDirectory, "mods"));
            logger.LogInformation(
                "Opening mod folder. InstanceId={InstanceId} ModsDirectory={ModsDirectory}",
                selectedInstance.Id,
                modsDirectory);

            if (!instanceFolderService.TryOpen(modsDirectory))
            {
                logger.LogWarning(
                    "Failed to open mod folder. InstanceId={InstanceId} ModsDirectory={ModsDirectory}",
                    selectedInstance.Id,
                    modsDirectory);
                statusService.Report(Strings.Status_OpenInstanceFolderFailed);
            }
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to prepare mod folder for opening. InstanceId={InstanceId}",
                selectedInstance.Id);
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
        }
    }

    [RelayCommand]
    private void ImportLocalMod()
    {
    }

    [RelayCommand]
    private void InstallOnlineMod()
    {
    }

    [RelayCommand]
    private void EnableAllMods()
    {
    }

    [RelayCommand]
    private void DisableAllMods()
    {
    }

    [RelayCommand]
    private void SelectMod(ModManagementModItemViewModel? mod)
    {
        if (mod is null)
        {
            SelectedMod = null;
            foreach (var item in Mods)
                item.IsSelected = false;
            return;
        }

        SelectedMod = mod;
        foreach (var item in Mods)
            item.IsSelected = ReferenceEquals(item, mod);
    }

    partial void OnInstalledModCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
    }

    partial void OnEnabledModCountChanged(int value)
    {
        OnPropertyChanged(nameof(InstalledSummaryText));
    }

    partial void OnModSearchQueryChanged(string value)
    {
        RefreshFromLocalMods();
        OnPropertyChanged(nameof(ModEmptyMessage));
    }

    private void RefreshSummary()
    {
        InstalledModCount = localModsViewModel.Mods.Count;
        EnabledModCount = localModsViewModel.Mods.Count(mod => mod.IsEnabled);
    }

    private void LocalModsViewModel_ModsChanged(object? sender, EventArgs e)
    {
        RefreshFromLocalMods();
    }

    private void RefreshFromLocalMods()
    {
        var selectedFullPath = SelectedMod?.FullPath;
        Mods.Clear();
        foreach (var mod in localModsViewModel.Mods.Where(MatchesSearch))
            Mods.Add(new ModManagementModItemViewModel(mod));

        RefreshSummary();
        OnPropertyChanged(nameof(HasMods));
        OnPropertyChanged(nameof(CanShowModEmptyState));
        OnPropertyChanged(nameof(ModEmptyMessage));

        var restoredSelection = Mods.FirstOrDefault(mod =>
            string.Equals(mod.FullPath, selectedFullPath, StringComparison.OrdinalIgnoreCase));
        SelectMod(restoredSelection ?? Mods.FirstOrDefault());
    }

    private bool MatchesSearch(LocalMod mod)
    {
        if (string.IsNullOrWhiteSpace(ModSearchQuery))
            return true;

        var query = ModSearchQuery.Trim();
        return Contains(mod.Name, query)
            || Contains(mod.Loader, query)
            || Contains(mod.ModId, query)
            || Contains(mod.Version, query)
            || Contains(mod.FileName, query);
    }

    private static bool Contains(string? source, string query)
    {
        return !string.IsNullOrWhiteSpace(source)
            && source.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
