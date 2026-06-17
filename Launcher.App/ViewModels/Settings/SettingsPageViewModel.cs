using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private const int AutoSaveDelayMilliseconds = 350;

    private readonly ISettingsService settingsService;
    private readonly IStatusService statusService;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private LauncherSettings settings = new();
    private CancellationTokenSource? autoSaveCancellationTokenSource;
    private bool hasPrimedSettings;
    private bool suppressAutoSave;

    [ObservableProperty]
    private SettingsSectionItem? selectedSection;

    [ObservableProperty]
    private string dataDirectory = string.Empty;

    [ObservableProperty]
    private string minecraftDirectory = string.Empty;

    [ObservableProperty]
    private string javaPath = string.Empty;

    [ObservableProperty]
    private SettingsMemoryOption? selectedMemoryOption;

    [ObservableProperty]
    private bool defaultCheckFilesBeforeLaunch = true;

    [ObservableProperty]
    private bool defaultAutoRepairMissingFiles = true;

    [ObservableProperty]
    private bool defaultMinimizeLauncherAfterLaunch;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IStatusService statusService)
    {
        this.settingsService = settingsService;
        this.statusService = statusService;

        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.General,
            Strings.Settings_SectionGeneral,
            "instance_setting_page/general_setting"));
        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.Launch,
            Strings.Settings_SectionLaunch,
            "instance_setting_page/launch"));
        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.JavaMemory,
            Strings.Settings_SectionJavaMemory,
            "instance_setting_page/java"));

        foreach (var memoryMb in new[] { 2048, 4096, 6144, 8192, 12288, 16384 })
            MemoryOptions.Add(new SettingsMemoryOption(memoryMb));

        SelectedSection = Sections[0];
    }

    public ObservableCollection<SettingsSectionItem> Sections { get; } = [];

    public ObservableCollection<SettingsMemoryOption> MemoryOptions { get; } = [];

    public string SectionTitle => SelectedSection?.Title ?? Strings.Settings_SectionGeneral;

    public bool IsGeneralSection => SelectedSection?.Section is SettingsPageSection.General;

    public bool IsLaunchSection => SelectedSection?.Section is SettingsPageSection.Launch;

    public bool IsJavaMemorySection => SelectedSection?.Section is SettingsPageSection.JavaMemory;

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        suppressAutoSave = true;
        try
        {
            DataDirectory = launcherSettings.DataDirectory;
            MinecraftDirectory = launcherSettings.MinecraftDirectory;
            JavaPath = launcherSettings.DefaultJavaPath ?? string.Empty;
            DefaultCheckFilesBeforeLaunch = launcherSettings.DefaultCheckFilesBeforeLaunch;
            DefaultAutoRepairMissingFiles = launcherSettings.DefaultAutoRepairMissingFiles;
            DefaultMinimizeLauncherAfterLaunch = launcherSettings.DefaultMinimizeLauncherAfterLaunch;
            SelectedMemoryOption = EnsureMemoryOption(launcherSettings.DefaultMemoryMb);
        }
        finally
        {
            suppressAutoSave = false;
        }

        hasPrimedSettings = true;
    }

    [RelayCommand]
    private void SelectSection(SettingsSectionItem? section)
    {
        if (section is null)
            return;

        SelectedSection = section;
    }

    partial void OnSelectedSectionChanged(SettingsSectionItem? value)
    {
        foreach (var section in Sections)
            section.IsSelected = ReferenceEquals(section, value);

        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsLaunchSection));
        OnPropertyChanged(nameof(IsJavaMemorySection));
    }

    partial void OnJavaPathChanged(string value)
    {
        ScheduleAutoSave();
    }

    partial void OnSelectedMemoryOptionChanged(SettingsMemoryOption? value)
    {
        ScheduleAutoSave();
    }

    partial void OnDefaultCheckFilesBeforeLaunchChanged(bool value)
    {
        if (!suppressAutoSave)
            ApplyLaunchCheckDependency(value, synchronizeAutoRepair: true);

        ScheduleAutoSave();
    }

    partial void OnDefaultAutoRepairMissingFilesChanged(bool value)
    {
        ScheduleAutoSave();
    }

    partial void OnDefaultMinimizeLauncherAfterLaunchChanged(bool value)
    {
        ScheduleAutoSave();
    }

    private void ApplySettings()
    {
        settings.DefaultJavaPath = string.IsNullOrWhiteSpace(JavaPath)
            ? null
            : JavaPath.Trim();
        settings.DefaultMemoryMb = SelectedMemoryOption?.MemoryMb ?? LauncherDefaults.DefaultMemoryMb;
        settings.DefaultCheckFilesBeforeLaunch = DefaultCheckFilesBeforeLaunch;
        settings.DefaultAutoRepairMissingFiles = DefaultAutoRepairMissingFiles;
        settings.DefaultMinimizeLauncherAfterLaunch = DefaultMinimizeLauncherAfterLaunch;

        suppressAutoSave = true;
        try
        {
            JavaPath = settings.DefaultJavaPath ?? string.Empty;
            SelectedMemoryOption = EnsureMemoryOption(settings.DefaultMemoryMb);
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private void ApplyLaunchCheckDependency(bool checkFilesBeforeLaunch, bool synchronizeAutoRepair)
    {
        if (!synchronizeAutoRepair)
            return;

        var targetAutoRepairValue = checkFilesBeforeLaunch;
        if (DefaultAutoRepairMissingFiles == targetAutoRepairValue)
            return;

        suppressAutoSave = true;
        try
        {
            DefaultAutoRepairMissingFiles = targetAutoRepairValue;
        }
        finally
        {
            suppressAutoSave = false;
        }
    }

    private SettingsMemoryOption EnsureMemoryOption(int memoryMb)
    {
        var existing = MemoryOptions.FirstOrDefault(option => option.MemoryMb == memoryMb);
        if (existing is not null)
            return existing;

        var created = new SettingsMemoryOption(memoryMb);
        var insertIndex = 0;
        while (insertIndex < MemoryOptions.Count && MemoryOptions[insertIndex].MemoryMb < memoryMb)
            insertIndex++;

        MemoryOptions.Insert(insertIndex, created);
        return created;
    }

    private void ScheduleAutoSave()
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        autoSaveCancellationTokenSource?.Cancel();
        autoSaveCancellationTokenSource?.Dispose();

        var cancellationTokenSource = new CancellationTokenSource();
        autoSaveCancellationTokenSource = cancellationTokenSource;
        _ = SaveAfterDelayAsync(cancellationTokenSource.Token);
    }

    private async Task SaveAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(AutoSaveDelayMilliseconds, cancellationToken);
            await SaveCoreAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SaveCoreAsync(CancellationToken cancellationToken)
    {
        var lockTaken = false;
        try
        {
            await saveLock.WaitAsync(cancellationToken);
            lockTaken = true;
            cancellationToken.ThrowIfCancellationRequested();
            ApplySettings();
            await settingsService.SaveAsync(settings, cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_SettingsSaveFailed);
        }
        finally
        {
            if (lockTaken)
                saveLock.Release();
        }
    }
}
