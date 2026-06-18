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
    private const string JavaSelectionAutoId = "auto";
    private const string JavaSelectionManualId = "manual";

    private readonly ISettingsService settingsService;
    private readonly IStatusService statusService;
    private readonly IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService;
    private readonly IFilePickerService filePickerService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private LauncherSettings settings = new();
    private CancellationTokenSource? autoSaveCancellationTokenSource;
    private CancellationTokenSource? javaRuntimeScanCancellationTokenSource;
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
    private SettingsJavaSelectionOption? selectedJavaSelectionOption;

    [ObservableProperty]
    private SettingsJavaRuntimeItem? selectedJavaRuntime;

    [ObservableProperty]
    private bool isJavaRuntimeScanRunning;

    [ObservableProperty]
    private string javaRuntimeListMessage = Strings.Settings_JavaListEmpty;

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
        this.javaRuntimeDiscoveryService = javaRuntimeDiscoveryService;
        this.filePickerService = filePickerService;
        this.floatingMessageService = floatingMessageService;

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

        JavaSelectionOptions.Add(new SettingsJavaSelectionOption(JavaSelectionAutoId, Strings.Settings_JavaSelectionAuto));
        JavaSelectionOptions.Add(new SettingsJavaSelectionOption(JavaSelectionManualId, Strings.Settings_JavaSelectionManual));
        SelectedJavaSelectionOption = JavaSelectionOptions[0];

        SelectedSection = Sections[0];
    }

    public ObservableCollection<SettingsSectionItem> Sections { get; } = [];

    public ObservableCollection<SettingsMemoryOption> MemoryOptions { get; } = [];

    public ObservableCollection<SettingsJavaSelectionOption> JavaSelectionOptions { get; } = [];

    public ObservableCollection<SettingsJavaRuntimeItem> JavaRuntimes { get; } = [];

    public event EventHandler? LaunchDefaultsChanged;

    public string SectionTitle => SelectedSection?.Title ?? Strings.Settings_SectionGeneral;

    public bool IsGeneralSection => SelectedSection?.Section is SettingsPageSection.General;

    public bool IsLaunchSection => SelectedSection?.Section is SettingsPageSection.Launch;

    public bool IsJavaMemorySection => SelectedSection?.Section is SettingsPageSection.JavaMemory;

    public bool HasJavaRuntimeListMessage => !string.IsNullOrWhiteSpace(JavaRuntimeListMessage);

    public bool IsJavaManualSelection => GetSelectedJavaSelectionMode() is JavaSelectionMode.Manual;

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
            SelectedJavaSelectionOption = GetJavaSelectionOption(launcherSettings.JavaSelectionMode);
            SelectedJavaRuntime = null;
        }
        finally
        {
            suppressAutoSave = false;
        }

        hasPrimedSettings = true;
        _ = RefreshJavaRuntimesAsync();
    }

    [RelayCommand]
    private void SelectSection(SettingsSectionItem? section)
    {
        if (section is null)
            return;

        SelectedSection = section;
    }

    [RelayCommand(CanExecute = nameof(CanRefreshJavaRuntimes))]
    private async Task RefreshJavaRuntimesAsync()
    {
        if (IsJavaRuntimeScanRunning)
            return;

        javaRuntimeScanCancellationTokenSource?.Cancel();
        javaRuntimeScanCancellationTokenSource?.Dispose();

        var cancellationTokenSource = new CancellationTokenSource();
        javaRuntimeScanCancellationTokenSource = cancellationTokenSource;

        IsJavaRuntimeScanRunning = true;
        JavaRuntimeListMessage = Strings.Settings_JavaListLoading;

        try
        {
            var discoveredRuntimes = await javaRuntimeDiscoveryService.DiscoverAsync(
                MinecraftDirectory,
                cancellationTokenSource.Token);

            JavaRuntimes.Clear();
            foreach (var runtime in discoveredRuntimes)
                JavaRuntimes.Add(new SettingsJavaRuntimeItem(runtime));

            await EnsureSavedSelectedJavaRuntimePresentAsync(cancellationTokenSource.Token);
            UpdateJavaRuntimeSelectionAfterListChanged();
            JavaRuntimeListMessage = JavaRuntimes.Count == 0
                ? Strings.Settings_JavaListEmpty
                : string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            JavaRuntimes.Clear();
            JavaRuntimeListMessage = Strings.Settings_JavaListEmpty;
            statusService.Report(Strings.Status_JavaScanFailed);
        }
        finally
        {
            if (ReferenceEquals(javaRuntimeScanCancellationTokenSource, cancellationTokenSource))
            {
                IsJavaRuntimeScanRunning = false;
                cancellationTokenSource.Dispose();
                javaRuntimeScanCancellationTokenSource = null;
            }
        }
    }

    [RelayCommand]
    private async Task ImportJavaRuntimeAsync()
    {
        var executablePath = filePickerService.PickJavaExecutable();
        if (string.IsNullOrWhiteSpace(executablePath))
            return;

        try
        {
            var runtime = await javaRuntimeDiscoveryService.DiscoverExecutableAsync(executablePath);
            if (!AddJavaRuntime(runtime))
            {
                floatingMessageService.Show(Strings.Status_JavaAlreadyExists);
                return;
            }

            UpdateJavaRuntimeSelectionAfterListChanged();
            JavaRuntimeListMessage = JavaRuntimes.Count == 0
                ? Strings.Settings_JavaListEmpty
                : string.Empty;
            statusService.Report(Strings.Status_JavaImported);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_JavaImportFailed);
        }
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

    partial void OnIsJavaRuntimeScanRunningChanged(bool value)
    {
        RefreshJavaRuntimesCommand.NotifyCanExecuteChanged();
    }

    partial void OnJavaRuntimeListMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasJavaRuntimeListMessage));
    }

    partial void OnSelectedJavaSelectionOptionChanged(SettingsJavaSelectionOption? value)
    {
        OnPropertyChanged(nameof(IsJavaManualSelection));

        if (IsJavaManualSelection)
        {
            UpdateJavaRuntimeSelectionAfterListChanged();
        }
        else
        {
            suppressAutoSave = true;
            try
            {
                SelectedJavaRuntime = null;
            }
            finally
            {
                suppressAutoSave = false;
            }
        }

        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnSelectedJavaRuntimeChanged(SettingsJavaRuntimeItem? value)
    {
        if (!IsJavaManualSelection)
        {
            if (value is not null)
            {
                suppressAutoSave = true;
                try
                {
                    SelectedJavaRuntime = null;
                }
                finally
                {
                    suppressAutoSave = false;
                }
            }

            return;
        }

        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
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

    private bool CanRefreshJavaRuntimes()
    {
        return !IsJavaRuntimeScanRunning;
    }

    private bool AddJavaRuntime(JavaRuntimeInfo runtime)
    {
        if (JavaRuntimes.Any(item => IsSameJavaRuntime(item, runtime)))
            return false;

        var newItem = new SettingsJavaRuntimeItem(runtime);
        var insertIndex = 0;
        while (insertIndex < JavaRuntimes.Count
            && (JavaRuntimes[insertIndex].MajorVersion ?? 0) > (newItem.MajorVersion ?? 0))
        {
            insertIndex++;
        }

        JavaRuntimes.Insert(insertIndex, newItem);
        return true;
    }

    private static bool IsSameJavaRuntime(SettingsJavaRuntimeItem item, JavaRuntimeInfo runtime)
    {
        if (IsSameExecutablePath(item.ExecutablePath, runtime.ExecutablePath))
            return true;

        if (string.IsNullOrWhiteSpace(runtime.Version))
            return false;

        return string.Equals(item.InstallationDirectory, runtime.InstallationDirectory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.VersionText, runtime.Version, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.Architecture, runtime.Architecture, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameExecutablePath(string? left, string? right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private SettingsJavaSelectionOption GetJavaSelectionOption(JavaSelectionMode mode)
    {
        var targetId = mode is JavaSelectionMode.Manual ? JavaSelectionManualId : JavaSelectionAutoId;
        return JavaSelectionOptions.FirstOrDefault(option => option.Id == targetId) ?? JavaSelectionOptions[0];
    }

    private JavaSelectionMode GetSelectedJavaSelectionMode()
    {
        return SelectedJavaSelectionOption?.Id == JavaSelectionManualId
            ? JavaSelectionMode.Manual
            : JavaSelectionMode.Auto;
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
        settings.JavaSelectionMode = GetSelectedJavaSelectionMode();
        if (settings.JavaSelectionMode is JavaSelectionMode.Manual && SelectedJavaRuntime is not null)
            settings.SelectedJavaExecutablePath = SelectedJavaRuntime.ExecutablePath;
    }

    private async Task EnsureSavedSelectedJavaRuntimePresentAsync(CancellationToken cancellationToken)
    {
        if (!IsJavaManualSelection || string.IsNullOrWhiteSpace(settings.SelectedJavaExecutablePath))
            return;

        if (JavaRuntimes.Any(item => IsSameExecutablePath(item.ExecutablePath, settings.SelectedJavaExecutablePath)))
            return;

        try
        {
            var runtime = await javaRuntimeDiscoveryService.DiscoverExecutableAsync(
                settings.SelectedJavaExecutablePath,
                cancellationToken);
            AddJavaRuntime(runtime);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }
    }

    private void UpdateJavaRuntimeSelectionAfterListChanged()
    {
        if (!IsJavaManualSelection)
        {
            SelectedJavaRuntime = null;
            return;
        }

        var savedPath = settings.SelectedJavaExecutablePath;
        var savedRuntime = string.IsNullOrWhiteSpace(savedPath)
            ? null
            : JavaRuntimes.FirstOrDefault(item => IsSameExecutablePath(item.ExecutablePath, savedPath));
        var currentRuntime = SelectedJavaRuntime is null
            ? null
            : JavaRuntimes.FirstOrDefault(item => IsSameExecutablePath(item.ExecutablePath, SelectedJavaRuntime.ExecutablePath));

        SelectedJavaRuntime = savedRuntime ?? currentRuntime ?? JavaRuntimes.FirstOrDefault();
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
