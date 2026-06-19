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
    private readonly ISystemMemoryService systemMemoryService;
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
    private SettingsMemoryModeOption? selectedMemoryModeOption;

    [ObservableProperty]
    private double defaultMemoryMb = LauncherDefaults.DefaultMemoryMb;

    [ObservableProperty]
    private int memorySliderMinimumMb = MemoryAllocationCalculator.MinimumMemoryMb;

    [ObservableProperty]
    private int memorySliderMaximumMb = MemoryAllocationCalculator.FallbackMaximumMemoryMb;

    [ObservableProperty]
    private string systemTotalMemoryText = string.Empty;

    [ObservableProperty]
    private string systemAvailableMemoryText = string.Empty;

    [ObservableProperty]
    private int automaticMemoryMb = LauncherDefaults.DefaultMemoryMb;

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

    [ObservableProperty]
    private string controlDemoInputText = Strings.Settings_ControlDemoDefaultInput;

    [ObservableProperty]
    private string controlDemoSearchText = string.Empty;

    [ObservableProperty]
    private string controlDemoStatusText = Strings.Settings_ControlDemoStatusReady;

    [ObservableProperty]
    private bool controlDemoSwitchEnabled = true;

    [ObservableProperty]
    private double controlDemoSliderValue = 48;

    [ObservableProperty]
    private bool controlDemoSecondaryMenuSelected = true;

    [ObservableProperty]
    private int controlDemoProgress = 64;

    [ObservableProperty]
    private SettingsInteractiveControlItem? selectedControlDemoComboOption;

    [ObservableProperty]
    private SettingsInteractiveControlItem? selectedInteractiveControl;

    public SettingsPageViewModel(
        ISettingsService settingsService,
        IStatusService statusService,
        ISystemMemoryService systemMemoryService,
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService)
    {
        this.settingsService = settingsService;
        this.statusService = statusService;
        this.systemMemoryService = systemMemoryService;
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
        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.ControlList,
            Strings.Settings_SectionControlList,
            "general/general_all_application"));

        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Auto,
            Strings.Settings_MemoryModeAuto));
        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Manual,
            Strings.Settings_MemoryModeManual));

        foreach (var control in SettingsInteractiveControlCatalog.Create())
            InteractiveControls.Add(control);

        SelectedMemoryModeOption = MemoryModeOptions[0];
        SelectedControlDemoComboOption = InteractiveControls.FirstOrDefault();
        SelectedInteractiveControl = InteractiveControls.FirstOrDefault();
        SelectedSection = Sections[0];
    }

    public ObservableCollection<SettingsSectionItem> Sections { get; } = [];

    public ObservableCollection<SettingsMemoryModeOption> MemoryModeOptions { get; } = [];

    public ObservableCollection<SettingsInteractiveControlItem> InteractiveControls { get; } = [];

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

    public bool IsControlListSection => SelectedSection?.Section is SettingsPageSection.ControlList;

    public bool HasJavaRuntimeListMessage => JavaSettings.HasJavaRuntimeListMessage;

    public bool IsJavaManualSelection => JavaSettings.IsJavaManualSelection;

    public bool IsMemorySliderEnabled => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public bool IsMemorySliderVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public string DefaultMemoryText => FormatMemorySizeGb(DefaultMemoryMb);

    public string AutomaticMemoryText => FormatMemorySizeGb(AutomaticMemoryMb);

    public string SystemMemorySummaryText => string.Format(
        Strings.Settings_SystemMemorySummaryFormat,
        SystemAvailableMemoryText,
        SystemTotalMemoryText);

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        settings = launcherSettings;
        RefreshSystemMemorySnapshot();
        suppressAutoSave = true;
        try
        {
            DataDirectory = launcherSettings.DataDirectory;
            MinecraftDirectory = launcherSettings.MinecraftDirectory;
            SelectedMemoryModeOption = ResolveMemoryModeOption(launcherSettings.DefaultMemorySettingsMode);
            DefaultMemoryMb = NormalizeMemoryValue(launcherSettings.DefaultMemoryMb);
            DefaultCheckFilesBeforeLaunch = launcherSettings.DefaultCheckFilesBeforeLaunch;
            DefaultAutoRepairMissingFiles = launcherSettings.DefaultAutoRepairMissingFiles;
            DefaultMinimizeLauncherAfterLaunch = launcherSettings.DefaultMinimizeLauncherAfterLaunch;
            DefaultLaunchFullScreen = launcherSettings.DefaultLaunchFullScreen;
            DefaultPreLaunchCommand = launcherSettings.DefaultPreLaunchCommand;
            DefaultWaitForPreLaunchCommand = launcherSettings.DefaultWaitForPreLaunchCommand;
            DefaultPostExitCommand = launcherSettings.DefaultPostExitCommand;
            DefaultJvmArguments = launcherSettings.DefaultJvmArguments;
            DefaultGameArguments = launcherSettings.DefaultGameArguments;
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

    [RelayCommand]
    private void RunControlDemoAction()
    {
        ControlDemoProgress = ControlDemoProgress >= 100 ? 20 : ControlDemoProgress + 20;
        ControlDemoStatusText = Strings.Settings_ControlDemoStatusClicked;
        ControlDemoSecondaryMenuSelected = !ControlDemoSecondaryMenuSelected;
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
        OnPropertyChanged(nameof(IsControlListSection));
    }

    partial void OnSelectedMemoryModeOptionChanged(SettingsMemoryModeOption? value)
    {
        OnPropertyChanged(nameof(IsMemorySliderEnabled));
        OnPropertyChanged(nameof(IsMemorySliderVisible));
        OnPropertyChanged(nameof(IsAutomaticMemorySummaryVisible));
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnDefaultMemoryMbChanged(double value)
    {
        var clamped = Math.Clamp(value, MemorySliderMinimumMb, MemorySliderMaximumMb);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            DefaultMemoryMb = clamped;
            return;
        }

        OnPropertyChanged(nameof(DefaultMemoryText));
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnSystemTotalMemoryTextChanged(string value)
    {
        OnPropertyChanged(nameof(SystemMemorySummaryText));
    }

    partial void OnSystemAvailableMemoryTextChanged(string value)
    {
        OnPropertyChanged(nameof(SystemMemorySummaryText));
    }

    partial void OnAutomaticMemoryMbChanged(int value)
    {
        OnPropertyChanged(nameof(AutomaticMemoryText));
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
        settings.DefaultMemorySettingsMode = SelectedMemoryModeOption?.Mode ?? MemorySettingsMode.Auto;
        settings.DefaultMemoryMb = NormalizeMemoryValue(DefaultMemoryMb);
        settings.DefaultCheckFilesBeforeLaunch = DefaultCheckFilesBeforeLaunch;
        settings.DefaultAutoRepairMissingFiles = DefaultAutoRepairMissingFiles;
        settings.DefaultMinimizeLauncherAfterLaunch = DefaultMinimizeLauncherAfterLaunch;
        settings.DefaultLaunchFullScreen = DefaultLaunchFullScreen;
        ApplyLaunchDefaultsToSettings();

        suppressAutoSave = true;
        try
        {
            SelectedMemoryModeOption = ResolveMemoryModeOption(settings.DefaultMemorySettingsMode);
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
        settings.DefaultMemorySettingsMode = SelectedMemoryModeOption?.Mode ?? MemorySettingsMode.Auto;
        settings.DefaultMemoryMb = NormalizeMemoryValue(DefaultMemoryMb);
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

    private SettingsMemoryModeOption ResolveMemoryModeOption(MemorySettingsMode mode)
    {
        return MemoryModeOptions.FirstOrDefault(option => option.Mode == mode)
               ?? MemoryModeOptions[0];
    }

    public void RefreshSystemMemorySnapshot()
    {
        try
        {
            var snapshot = systemMemoryService.GetSnapshot();
            var totalMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.TotalMemoryBytes);
            var availableMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.AvailableMemoryBytes);
            MemorySliderMaximumMb = CalculateMemorySliderMaximumMb(totalMemoryMb);
            AutomaticMemoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(snapshot);
            SystemTotalMemoryText = FormatMemorySize(totalMemoryMb);
            SystemAvailableMemoryText = FormatMemorySizeGb(availableMemoryMb);
        }
        catch (Exception)
        {
            MemorySliderMaximumMb = MemoryAllocationCalculator.FallbackMaximumMemoryMb;
            AutomaticMemoryMb = NormalizeMemoryValue(settings.DefaultMemoryMb);
            SystemTotalMemoryText = Strings.Settings_MemoryUnavailable;
            SystemAvailableMemoryText = Strings.Settings_MemoryUnavailable;
        }
    }

    public static int CalculateMemorySliderMaximumMb(int totalMemoryMb)
    {
        return MemoryAllocationCalculator.CalculateMaximumMemoryMb(totalMemoryMb);
    }

    public bool IsAutomaticMemorySummaryVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Auto;

    private int NormalizeMemoryValue(double memoryMb)
    {
        return MemoryAllocationCalculator.NormalizeRecordedMemoryMb(memoryMb, MemorySliderMaximumMb);
    }

    private static string FormatMemorySize(int memoryMb)
    {
        if (memoryMb >= 1024)
            return string.Format(Strings.Settings_MemorySizeGbFormat, memoryMb / 1024d);

        return string.Format(Strings.Settings_MemorySizeMbFormat, memoryMb);
    }

    private static string FormatMemorySizeGb(double memoryMb)
    {
        return string.Format(Strings.Settings_MemorySizeGbFormat, memoryMb / 1024d);
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
