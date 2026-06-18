using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private SettingsMemoryOption? selectedMemoryOption;

    [ObservableProperty]
    private bool defaultCheckFilesBeforeLaunch = true;

    [ObservableProperty]
    private bool defaultAutoRepairMissingFiles = true;

    [ObservableProperty]
    private bool defaultMinimizeLauncherAfterLaunch;

    [ObservableProperty]
    private bool defaultLaunchFullScreen;

    [ObservableProperty]
    private string defaultPreLaunchCommand = string.Empty;

    [ObservableProperty]
    private bool defaultWaitForPreLaunchCommand = true;

    [ObservableProperty]
    private string defaultPostExitCommand = string.Empty;

    [ObservableProperty]
    private string defaultJvmArguments = string.Empty;

    [ObservableProperty]
    private string defaultGameArguments = string.Empty;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IStatusService statusService,
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService)
    {
        this.settingsService = settingsService;
        this.statusService = statusService;
        JavaSettings = new JavaSettingsEditorViewModel(
            javaRuntimeDiscoveryService,
            statusService,
            filePickerService,
            floatingMessageService,
            () => MinecraftDirectory);
        JavaSettings.PropertyChanged += JavaSettings_PropertyChanged;
        JavaSettings.JavaSelectionChanged += JavaSettings_JavaSelectionChanged;

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

    public JavaSettingsEditorViewModel JavaSettings { get; }

    public event EventHandler? LaunchDefaultsChanged;

    public ObservableCollection<SettingsJavaSelectionOption> JavaSelectionOptions => JavaSettings.JavaSelectionOptions;

    public ObservableCollection<SettingsJavaRuntimeItem> JavaRuntimes => JavaSettings.JavaRuntimes;

    public SettingsJavaSelectionOption? SelectedJavaSelectionOption
    {
        get => JavaSettings.SelectedJavaSelectionOption;
        set => JavaSettings.SelectedJavaSelectionOption = value;
    }

    public SettingsJavaRuntimeItem? SelectedJavaRuntime
    {
        get => JavaSettings.SelectedJavaRuntime;
        set => JavaSettings.SelectedJavaRuntime = value;
    }

    public bool IsJavaRuntimeScanRunning => JavaSettings.IsJavaRuntimeScanRunning;

    public string JavaRuntimeListMessage => JavaSettings.JavaRuntimeListMessage;

    public IAsyncRelayCommand RefreshJavaRuntimesCommand => JavaSettings.RefreshJavaRuntimesCommand;

    public IAsyncRelayCommand ImportJavaRuntimeCommand => JavaSettings.ImportJavaRuntimeCommand;

    public string SectionTitle => SelectedSection?.Title ?? Strings.Settings_SectionGeneral;

    public bool IsGeneralSection => SelectedSection?.Section is SettingsPageSection.General;

    public bool IsLaunchSection => SelectedSection?.Section is SettingsPageSection.Launch;

    public bool IsJavaMemorySection => SelectedSection?.Section is SettingsPageSection.JavaMemory;

    public bool HasJavaRuntimeListMessage => JavaSettings.HasJavaRuntimeListMessage;

    public bool IsJavaManualSelection => JavaSettings.IsJavaManualSelection;

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        suppressAutoSave = true;
        try
        {
            DataDirectory = launcherSettings.DataDirectory;
            MinecraftDirectory = launcherSettings.MinecraftDirectory;
            DefaultCheckFilesBeforeLaunch = launcherSettings.DefaultCheckFilesBeforeLaunch;
            DefaultAutoRepairMissingFiles = launcherSettings.DefaultAutoRepairMissingFiles;
            DefaultMinimizeLauncherAfterLaunch = launcherSettings.DefaultMinimizeLauncherAfterLaunch;
            DefaultLaunchFullScreen = launcherSettings.DefaultLaunchFullScreen;
            DefaultPreLaunchCommand = launcherSettings.DefaultPreLaunchCommand;
            DefaultWaitForPreLaunchCommand = launcherSettings.DefaultWaitForPreLaunchCommand;
            DefaultPostExitCommand = launcherSettings.DefaultPostExitCommand;
            DefaultJvmArguments = launcherSettings.DefaultJvmArguments;
            DefaultGameArguments = launcherSettings.DefaultGameArguments;
            SelectedMemoryOption = EnsureMemoryOption(launcherSettings.DefaultMemoryMb);
            JavaSettings.LoadSelection(launcherSettings.JavaSelectionMode, launcherSettings.SelectedJavaExecutablePath);
        }
        finally
        {
            suppressAutoSave = false;
        }

        hasPrimedSettings = true;
        _ = RefreshJavaRuntimesCommand.ExecuteAsync(null);
    }

    public void ShowJavaMemorySection()
    {
        SelectSectionCore(Sections.FirstOrDefault(section => section.Section is SettingsPageSection.JavaMemory));
    }

    [RelayCommand]
    private void SelectSection(SettingsSectionItem? section)
    {
        SelectSectionCore(section);
    }

    private void SelectSectionCore(SettingsSectionItem? section)
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

    partial void OnSelectedMemoryOptionChanged(SettingsMemoryOption? value)
    {
        ScheduleAutoSave();
    }

    partial void OnDefaultCheckFilesBeforeLaunchChanged(bool value)
    {
        if (!suppressAutoSave)
            ApplyLaunchCheckDependency(value, synchronizeAutoRepair: true);

        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultAutoRepairMissingFilesChanged(bool value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultMinimizeLauncherAfterLaunchChanged(bool value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultLaunchFullScreenChanged(bool value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultPreLaunchCommandChanged(string value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultWaitForPreLaunchCommandChanged(bool value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultPostExitCommandChanged(string value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultJvmArgumentsChanged(string value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultGameArgumentsChanged(string value)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    private void JavaSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
            return;

        OnPropertyChanged(e.PropertyName);
    }

    private void JavaSettings_JavaSelectionChanged(object? sender, EventArgs e)
    {
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    private void ApplySettings()
    {
        settings.DefaultMemoryMb = SelectedMemoryOption?.MemoryMb ?? LauncherDefaults.DefaultMemoryMb;
        settings.DefaultCheckFilesBeforeLaunch = DefaultCheckFilesBeforeLaunch;
        settings.DefaultAutoRepairMissingFiles = DefaultAutoRepairMissingFiles;
        settings.DefaultMinimizeLauncherAfterLaunch = DefaultMinimizeLauncherAfterLaunch;
        settings.DefaultLaunchFullScreen = DefaultLaunchFullScreen;
        ApplyLaunchDefaultsToSettings();

        suppressAutoSave = true;
        try
        {
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

    private void NotifyLaunchDefaultsChanged()
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        ApplyLaunchDefaultsToSettings();
        LaunchDefaultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyLaunchDefaultsToSettings()
    {
        settings.DefaultCheckFilesBeforeLaunch = DefaultCheckFilesBeforeLaunch;
        settings.DefaultAutoRepairMissingFiles = DefaultAutoRepairMissingFiles;
        settings.DefaultMinimizeLauncherAfterLaunch = DefaultMinimizeLauncherAfterLaunch;
        settings.DefaultLaunchFullScreen = DefaultLaunchFullScreen;
        settings.DefaultPreLaunchCommand = NormalizeSettingText(DefaultPreLaunchCommand);
        settings.DefaultWaitForPreLaunchCommand = DefaultWaitForPreLaunchCommand;
        settings.DefaultPostExitCommand = NormalizeSettingText(DefaultPostExitCommand);
        settings.DefaultJvmArguments = NormalizeSettingText(DefaultJvmArguments);
        settings.DefaultGameArguments = NormalizeSettingText(DefaultGameArguments);
        settings.JavaSelectionMode = JavaSettings.SelectedMode;
        if (settings.JavaSelectionMode is JavaSelectionMode.Manual
            && !string.IsNullOrWhiteSpace(JavaSettings.SelectedExecutablePath))
        {
            settings.SelectedJavaExecutablePath = JavaSettings.SelectedExecutablePath;
        }
    }

    private static string NormalizeSettingText(string? value)
    {
        return value?.Trim() ?? string.Empty;
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
