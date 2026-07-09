using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Logging;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.Application.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class SettingsPageViewModel : ObservableObject
{
    private const int AutoSaveDelayMilliseconds = 350;
    private readonly ISettingsService settingsService;
    private readonly IStatusService statusService;
    private readonly ISystemMemoryService systemMemoryService;
    private readonly IFilePickerService filePickerService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly IThemeService themeService;
    private readonly SemaphoreSlim saveLock = new(1, 1);
    private LauncherSettings settings = new();
    private CancellationTokenSource? autoSaveCancellationTokenSource;
    private bool hasPrimedSettings;
    private bool suppressAutoSave;

    [ObservableProperty]
    private SettingsSectionItem? selectedSection;

    [ObservableProperty]
    private SettingsSectionViewModelBase? currentSectionViewModel;

    [ObservableProperty]
    private string dataDirectory = string.Empty;

    [ObservableProperty]
    private string minecraftDirectory = string.Empty;

    [ObservableProperty]
    private string launcherLogDirectory = string.Empty;

    [ObservableProperty]
    private SettingsDownloadSourceOption? selectedDownloadSourceOption;

    [ObservableProperty]
    private string downloadSpeedLimitMbPerSecondText = string.Empty;

    [ObservableProperty]
    private SettingsMemoryModeOption? selectedMemoryModeOption;

    [ObservableProperty]
    private SettingsThemeOption? selectedThemeOption;

    [ObservableProperty]
    private SettingsAccentColorOption? selectedAccentColorOption;

    [ObservableProperty]
    private SettingsUpdateChannelOption? selectedUpdateChannelOption;

    [ObservableProperty]
    private bool followSystemTheme = true;

    [ObservableProperty]
    private int launcherBackgroundOpacityPercent = LauncherDefaults.DefaultLauncherBackgroundOpacityPercent;

    [ObservableProperty]
    private bool disableBackgroundBlur;

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
    private string controlDemoMultilineText = Strings.Settings_ControlDemoDefaultMultilineInput;

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
        IInstanceFolderService instanceFolderService,
        IFloatingMessageService floatingMessageService,
        IThemeService themeService,
        IExternalLinkService? externalLinkService = null,
        ILauncherUpdateService? launcherUpdateService = null,
        ILauncherSelfUpdateService? launcherSelfUpdateService = null,
        IApplicationExitService? applicationExitService = null,
        ILogger<InfoSettingsViewModel>? infoSettingsLogger = null)
    {
        this.settingsService = settingsService;
        this.statusService = statusService;
        this.systemMemoryService = systemMemoryService;
        this.filePickerService = filePickerService;
        this.instanceFolderService = instanceFolderService;
        this.themeService = themeService;
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
            SettingsPageSection.Language,
            Strings.Settings_SectionLanguage,
            "setting_page/earth"));
        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.LaunchMemory,
            Strings.Settings_SectionLaunchMemory,
            "instance_setting_page/launch"));
        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.Java,
            Strings.Settings_SectionJava,
            "instance_setting_page/java"));
        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.Theme,
            Strings.Settings_SectionTheme,
            "setting_page/theme"));
        Sections.Add(new SettingsSectionItem(
            SettingsPageSection.Info,
            Strings.Settings_SectionInfo,
            "setting_page/info"));

        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Auto,
            Strings.Settings_MemoryModeAuto));
        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Manual,
            Strings.Settings_MemoryModeManual));

        DownloadSourceOptions.Add(new SettingsDownloadSourceOption(
            DownloadSourcePreference.Auto,
            Strings.Settings_DownloadSourceAuto));
        DownloadSourceOptions.Add(new SettingsDownloadSourceOption(
            DownloadSourcePreference.Official,
            Strings.Settings_DownloadSourceOfficial));
        DownloadSourceOptions.Add(new SettingsDownloadSourceOption(
            DownloadSourcePreference.BmclApi,
            Strings.Settings_DownloadSourceBmclApi));

        ThemeOptions.Add(new SettingsThemeOption(
            LauncherDefaults.DefaultTheme,
            Strings.Settings_ThemeDarkTitle));
        ThemeOptions.Add(new SettingsThemeOption(
            "Light",
            Strings.Settings_ThemeLightTitle));

        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Blue,
            Strings.Settings_AccentColorBlueTitle));
        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Cyan,
            Strings.Settings_AccentColorCyanTitle));
        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Green,
            Strings.Settings_AccentColorGreenTitle));
        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Emerald,
            Strings.Settings_AccentColorEmeraldTitle));
        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Purple,
            Strings.Settings_AccentColorPurpleTitle));
        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Pink,
            Strings.Settings_AccentColorPinkTitle));
        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Orange,
            Strings.Settings_AccentColorOrangeTitle));
        AccentColorOptions.Add(new SettingsAccentColorOption(
            LauncherAccentColors.Amber,
            Strings.Settings_AccentColorAmberTitle));

        UpdateChannelOptions.Add(new SettingsUpdateChannelOption(
            LauncherUpdateChannel.Release,
            Strings.Settings_UpdateChannelReleaseTitle));
        UpdateChannelOptions.Add(new SettingsUpdateChannelOption(
            LauncherUpdateChannel.Beta,
            Strings.Settings_UpdateChannelBetaTitle));

        foreach (var control in SettingsInteractiveControlCatalog.Create())
            InteractiveControls.Add(control);

        SelectedDownloadSourceOption = DownloadSourceOptions[0];
        SelectedMemoryModeOption = MemoryModeOptions[0];
        SelectedThemeOption = ThemeOptions[0];
        SelectedAccentColorOption = AccentColorOptions[0];
        SelectedUpdateChannelOption = UpdateChannelOptions[0];
        SelectedControlDemoComboOption = InteractiveControls.FirstOrDefault();
        SelectedInteractiveControl = InteractiveControls.FirstOrDefault();
        General = new GeneralSettingsViewModel(this);
        Language = new LanguageSettingsViewModel(this);
        LaunchMemory = new LaunchMemorySettingsViewModel(this);
        Java = new JavaSettingsViewModel(this);
        Theme = new ThemeSettingsViewModel(this);
        Info = new InfoSettingsViewModel(
            this,
            statusService,
            floatingMessageService,
            externalLinkService ?? NullExternalLinkService.Instance,
            launcherUpdateService ?? NullLauncherUpdateService.Instance,
            launcherSelfUpdateService ?? NullLauncherSelfUpdateService.Instance,
            applicationExitService ?? NullApplicationExitService.Instance,
            infoSettingsLogger);
        ControlList = new ControlListSettingsViewModel(this);
        SelectedSection = Sections[0];
    }

    public ObservableCollection<SettingsSectionItem> Sections { get; } = [];

    public ObservableCollection<SettingsDownloadSourceOption> DownloadSourceOptions { get; } = [];

    public ObservableCollection<SettingsMemoryModeOption> MemoryModeOptions { get; } = [];

    public ObservableCollection<SettingsThemeOption> ThemeOptions { get; } = [];

    public ObservableCollection<SettingsAccentColorOption> AccentColorOptions { get; } = [];

    public ObservableCollection<SettingsUpdateChannelOption> UpdateChannelOptions { get; } = [];

    public ObservableCollection<SettingsInteractiveControlItem> InteractiveControls { get; } = [];

    public JavaSettingsEditorViewModel JavaSettings { get; }

    public GeneralSettingsViewModel General { get; }

    public LanguageSettingsViewModel Language { get; }

    public LaunchMemorySettingsViewModel LaunchMemory { get; }

    public JavaSettingsViewModel Java { get; }

    public ThemeSettingsViewModel Theme { get; }

    public InfoSettingsViewModel Info { get; }

    public ControlListSettingsViewModel ControlList { get; }

    public event EventHandler? LaunchDefaultsChanged;

    public event EventHandler<SettingsDownloadSourceChangedEventArgs>? DownloadSourceChanged;

    public event EventHandler<SettingsDownloadSpeedLimitChangedEventArgs>? DownloadSpeedLimitChanged;

    public event EventHandler<SettingsMinecraftDirectoryChangedEventArgs>? MinecraftDirectoryChanged;

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

    public bool IsLanguageSection => SelectedSection?.Section is SettingsPageSection.Language;

    public bool IsLaunchMemorySection => SelectedSection?.Section is SettingsPageSection.LaunchMemory;

    public bool IsJavaSection => SelectedSection?.Section is SettingsPageSection.Java;

    public bool IsThemeSection => SelectedSection?.Section is SettingsPageSection.Theme;

    public bool IsInfoSection => SelectedSection?.Section is SettingsPageSection.Info;

    public bool IsControlListSection => SelectedSection?.Section is SettingsPageSection.ControlList;

    public bool IsThemeSelectionVisible => !FollowSystemTheme;

    public string LauncherBackgroundOpacityText => $"{LauncherBackgroundOpacityPercent}%";

    public bool HasJavaRuntimeListMessage => JavaSettings.HasJavaRuntimeListMessage;

    public bool IsJavaManualSelection => JavaSettings.IsJavaManualSelection;

    public bool IsMemorySliderEnabled => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public bool IsMemorySliderVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public string DefaultMemoryText => MemorySizeTextFormatter.FormatGb(DefaultMemoryMb);

    public string AutomaticMemoryText => MemorySizeTextFormatter.FormatGb(AutomaticMemoryMb);

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
            LauncherLogDirectory = ResolveLauncherLogDirectory();
            SelectedDownloadSourceOption = ResolveDownloadSourceOption(launcherSettings.DownloadSourcePreference);
            DownloadSpeedLimitMbPerSecondText = FormatDownloadSpeedLimit(launcherSettings.DownloadSpeedLimitMbPerSecond);
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
            FollowSystemTheme = launcherSettings.ThemeFollowSystem;
            SelectedThemeOption = ResolveThemeOption(launcherSettings.Theme);
            SelectedAccentColorOption = ResolveAccentColorOption(launcherSettings.AccentColor);
            SelectedUpdateChannelOption = ResolveUpdateChannelOption(launcherSettings.UpdateChannel);
            Language.LoadSelection(
                launcherSettings.LauncherLanguage,
                launcherSettings.AutoSetGameLanguageToLauncherLanguage);
            DisableBackgroundBlur = launcherSettings.DisableBackgroundBlur;
            LauncherBackgroundOpacityPercent = NormalizeLauncherBackgroundOpacity(launcherSettings.LauncherBackgroundOpacityPercent);
            JavaSettings.LoadSelection(launcherSettings.JavaSelectionMode, launcherSettings.SelectedJavaExecutablePath);
        }
        finally
        {
            suppressAutoSave = false;
        }

        hasPrimedSettings = true;
        _ = RefreshJavaRuntimesCommand.ExecuteAsync(null);
    }

    public void ShowJavaSection()
    {
        SelectSectionCore(Sections.FirstOrDefault(section => section.Section is SettingsPageSection.Java));
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

    [RelayCommand]
    private void OpenMinecraftDirectory()
    {
        try
        {
            var directory = EnsureMinecraftDirectoryExists(MinecraftDirectory);
            if (!instanceFolderService.TryOpen(directory))
                statusService.Report(Strings.Status_OpenMinecraftDirectoryFailed);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_OpenMinecraftDirectoryFailed);
        }
    }

    [RelayCommand]
    private void OpenLauncherLogDirectory()
    {
        try
        {
            var directory = ResolveLauncherLogDirectory();
            directory = instanceFolderService.EnsureDirectoryExists(directory);
            LauncherLogDirectory = directory;
            if (!instanceFolderService.TryOpen(directory))
                statusService.Report(Strings.Status_OpenLaunchLogFolderFailed);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_OpenLaunchLogFolderFailed);
        }
    }

    [RelayCommand]
    private async Task ChangeMinecraftDirectoryAsync()
    {
        var selectedDirectory = filePickerService.PickFolder(
            Strings.FilePicker_MinecraftDirectoryTitle,
            MinecraftDirectory);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return;

        string normalizedDirectory;
        try
        {
            normalizedDirectory = EnsureMinecraftDirectoryExists(selectedDirectory);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_MinecraftDirectoryChangeFailed);
            return;
        }

        if (string.Equals(MinecraftDirectory, normalizedDirectory, StringComparison.OrdinalIgnoreCase))
            return;

        CancelPendingAutoSave();

        var previousDirectory = settings.MinecraftDirectory;
        var lockTaken = false;
        try
        {
            await saveLock.WaitAsync();
            lockTaken = true;

            ApplySettings();
            settings.MinecraftDirectory = normalizedDirectory;
            await settingsService.SaveAsync(settings);
            MinecraftDirectory = normalizedDirectory;
        }
        catch (Exception)
        {
            settings.MinecraftDirectory = previousDirectory;
            MinecraftDirectory = previousDirectory;
            statusService.Report(Strings.Status_MinecraftDirectoryChangeFailed);
            return;
        }
        finally
        {
            if (lockTaken)
                saveLock.Release();
        }

        statusService.Report(Strings.Status_MinecraftDirectoryChanged);
        MinecraftDirectoryChanged?.Invoke(this, new SettingsMinecraftDirectoryChangedEventArgs(normalizedDirectory));
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
        OnPropertyChanged(nameof(IsLanguageSection));
        OnPropertyChanged(nameof(IsLaunchMemorySection));
        OnPropertyChanged(nameof(IsJavaSection));
        OnPropertyChanged(nameof(IsThemeSection));
        OnPropertyChanged(nameof(IsInfoSection));
        OnPropertyChanged(nameof(IsControlListSection));
        CurrentSectionViewModel = value?.Section switch
        {
            SettingsPageSection.General => General,
            SettingsPageSection.Language => Language,
            SettingsPageSection.LaunchMemory => LaunchMemory,
            SettingsPageSection.Java => Java,
            SettingsPageSection.Theme => Theme,
            SettingsPageSection.Info => Info,
            SettingsPageSection.ControlList => ControlList,
            _ => General
        };
    }

    partial void OnSelectedDownloadSourceOptionChanged(SettingsDownloadSourceOption? value)
    {
        ScheduleAutoSave();
        NotifyDownloadSourceChanged();
    }

    partial void OnDownloadSpeedLimitMbPerSecondTextChanged(string value)
    {
        NotifyDownloadSpeedLimitChanged();
        ScheduleAutoSave();
    }

    partial void OnSelectedMemoryModeOptionChanged(SettingsMemoryModeOption? value)
    {
        OnPropertyChanged(nameof(IsMemorySliderEnabled));
        OnPropertyChanged(nameof(IsMemorySliderVisible));
        OnPropertyChanged(nameof(IsAutomaticMemorySummaryVisible));
        ScheduleAutoSave();
        NotifyLaunchDefaultsChanged();
    }

    partial void OnFollowSystemThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsThemeSelectionVisible));
        ApplyThemePreference();
        ScheduleAutoSave();
    }

    partial void OnSelectedThemeOptionChanged(SettingsThemeOption? value)
    {
        ApplyThemePreference();
        ScheduleAutoSave();
    }

    partial void OnSelectedAccentColorOptionChanged(SettingsAccentColorOption? value)
    {
        ApplyAccentPreference();
        ScheduleAutoSave();
    }

    partial void OnSelectedUpdateChannelOptionChanged(SettingsUpdateChannelOption? value)
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        settings.UpdateChannel = value?.Channel ?? LauncherDefaults.DefaultUpdateChannel;
        ScheduleAutoSave();
    }

    internal void NotifyLanguagePreferenceChanged()
    {
        ScheduleAutoSave();
    }

    partial void OnLauncherBackgroundOpacityPercentChanged(int value)
    {
        var normalized = NormalizeLauncherBackgroundOpacity(value);
        if (normalized != value)
        {
            LauncherBackgroundOpacityPercent = normalized;
            return;
        }

        OnPropertyChanged(nameof(LauncherBackgroundOpacityText));
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        settings.LauncherBackgroundOpacityPercent = normalized;
        themeService.ApplyBackgroundOpacity(normalized);
        ScheduleAutoSave();
    }

    partial void OnDisableBackgroundBlurChanged(bool value)
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        settings.DisableBackgroundBlur = value;
        themeService.ApplyBackgroundBlurDisabled(value);
        ScheduleAutoSave();
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
        settings.MinecraftDirectory = NormalizeDirectoryPath(MinecraftDirectory, settings.MinecraftDirectory);
        settings.Theme = SelectedThemeOption?.Id ?? LauncherDefaults.DefaultTheme;
        settings.AccentColor = SelectedAccentColorOption?.Id ?? LauncherDefaults.DefaultAccentColor;
        settings.LauncherLanguage = Language.SelectedLanguageId;
        settings.AutoSetGameLanguageToLauncherLanguage = Language.AutoSetGameLanguageToLauncherLanguage;
        settings.UpdateChannel = SelectedUpdateChannelOption?.Channel ?? LauncherDefaults.DefaultUpdateChannel;
        settings.ThemeFollowSystem = FollowSystemTheme;
        settings.DisableBackgroundBlur = DisableBackgroundBlur;
        settings.LauncherBackgroundOpacityPercent = NormalizeLauncherBackgroundOpacity(LauncherBackgroundOpacityPercent);
        settings.DownloadSourcePreference = SelectedDownloadSourceOption?.Preference ?? DownloadSourcePreference.Auto;
        settings.DownloadSpeedLimitMbPerSecond = NormalizeDownloadSpeedLimit(DownloadSpeedLimitMbPerSecondText);
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
            DownloadSpeedLimitMbPerSecondText = FormatDownloadSpeedLimit(settings.DownloadSpeedLimitMbPerSecond);
            SelectedMemoryModeOption = ResolveMemoryModeOption(settings.DefaultMemorySettingsMode);
            SelectedThemeOption = ResolveThemeOption(settings.Theme);
            SelectedAccentColorOption = ResolveAccentColorOption(settings.AccentColor);
            SelectedUpdateChannelOption = ResolveUpdateChannelOption(settings.UpdateChannel);
            Language.LoadSelection(
                settings.LauncherLanguage,
                settings.AutoSetGameLanguageToLauncherLanguage);
            DisableBackgroundBlur = settings.DisableBackgroundBlur;
            LauncherBackgroundOpacityPercent = settings.LauncherBackgroundOpacityPercent;
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

    private void NotifyDownloadSourceChanged()
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        settings.DownloadSourcePreference = SelectedDownloadSourceOption?.Preference ?? DownloadSourcePreference.Auto;
        DownloadSourceChanged?.Invoke(this, new SettingsDownloadSourceChangedEventArgs(settings.DownloadSourcePreference));
    }

    private void NotifyDownloadSpeedLimitChanged()
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        settings.DownloadSpeedLimitMbPerSecond = NormalizeDownloadSpeedLimit(DownloadSpeedLimitMbPerSecondText);
        DownloadSpeedLimitChanged?.Invoke(
            this,
            new SettingsDownloadSpeedLimitChangedEventArgs(settings.DownloadSpeedLimitMbPerSecond));
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

    private static int NormalizeDownloadSpeedLimit(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return 0;

        return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(parsed, 0)
            : 0;
    }

    private static string FormatDownloadSpeedLimit(int value)
    {
        return value > 0
            ? value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static string NormalizeDirectoryPath(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value)
            ? fallback
            : Path.GetFullPath(value);
    }

    private SettingsMemoryModeOption ResolveMemoryModeOption(MemorySettingsMode mode)
    {
        return MemoryModeOptions.FirstOrDefault(option => option.Mode == mode)
               ?? MemoryModeOptions[0];
    }

    private SettingsDownloadSourceOption ResolveDownloadSourceOption(DownloadSourcePreference preference)
    {
        return DownloadSourceOptions.FirstOrDefault(option => option.Preference == preference)
               ?? DownloadSourceOptions[0];
    }

    private SettingsThemeOption ResolveThemeOption(string? theme)
    {
        return ThemeOptions.FirstOrDefault(option => string.Equals(option.Id, theme, StringComparison.OrdinalIgnoreCase))
               ?? ThemeOptions[0];
    }

    private SettingsAccentColorOption ResolveAccentColorOption(string? accentColor)
    {
        return AccentColorOptions.FirstOrDefault(option => string.Equals(option.Id, accentColor, StringComparison.OrdinalIgnoreCase))
               ?? AccentColorOptions[0];
    }

    private SettingsUpdateChannelOption ResolveUpdateChannelOption(LauncherUpdateChannel channel)
    {
        return UpdateChannelOptions.FirstOrDefault(option => option.Channel == channel)
               ?? UpdateChannelOptions[0];
    }

    private static int NormalizeLauncherBackgroundOpacity(int value)
    {
        return Math.Clamp(value, 0, 100);
    }

    private void ApplyThemePreference()
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        settings.Theme = SelectedThemeOption?.Id ?? LauncherDefaults.DefaultTheme;
        settings.ThemeFollowSystem = FollowSystemTheme;
        settings.DisableBackgroundBlur = DisableBackgroundBlur;
        settings.LauncherBackgroundOpacityPercent = NormalizeLauncherBackgroundOpacity(LauncherBackgroundOpacityPercent);
        themeService.ApplyPreference(
            settings.Theme,
            settings.ThemeFollowSystem,
            settings.LauncherBackgroundOpacityPercent,
            settings.DisableBackgroundBlur);
    }

    private void ApplyAccentPreference()
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        settings.AccentColor = SelectedAccentColorOption?.Id ?? LauncherDefaults.DefaultAccentColor;
        themeService.ApplyAccent(settings.AccentColor);
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
            SystemTotalMemoryText = MemorySizeTextFormatter.Format(totalMemoryMb);
            SystemAvailableMemoryText = MemorySizeTextFormatter.FormatGb(availableMemoryMb);
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

    private void ScheduleAutoSave()
    {
        if (suppressAutoSave || !hasPrimedSettings)
            return;

        CancelPendingAutoSave();

        var cancellationTokenSource = new CancellationTokenSource();
        autoSaveCancellationTokenSource = cancellationTokenSource;
        _ = SaveAfterDelayAsync(cancellationTokenSource.Token);
    }

    private void CancelPendingAutoSave()
    {
        autoSaveCancellationTokenSource?.Cancel();
        autoSaveCancellationTokenSource?.Dispose();
        autoSaveCancellationTokenSource = null;
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

    private string EnsureMinecraftDirectoryExists(string directory)
    {
        return instanceFolderService.EnsureDirectoryExists(directory);
    }

    private static string ResolveLauncherLogDirectory()
    {
        return Path.GetFullPath(LauncherLogConfiguration.ResolveLogDirectory());
    }

    private sealed class NullExternalLinkService : IExternalLinkService
    {
        public static readonly NullExternalLinkService Instance = new();

        private NullExternalLinkService()
        {
        }

        public bool TryOpen(string url)
        {
            return false;
        }
    }

    private sealed class NullLauncherUpdateService : ILauncherUpdateService
    {
        public static readonly NullLauncherUpdateService Instance = new();

        private NullLauncherUpdateService()
        {
        }

        public Task<LauncherUpdateCheckResult> CheckForUpdatesAsync(
            string currentVersion,
            LauncherUpdateChannel channel,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LauncherUpdateCheckResult.Failed(currentVersion));
        }
    }

    private sealed class NullLauncherSelfUpdateService : ILauncherSelfUpdateService
    {
        public static readonly NullLauncherSelfUpdateService Instance = new();

        private NullLauncherSelfUpdateService()
        {
        }

        public Task<LauncherSelfUpdateStartResult> StartUpdateAsync(
            LauncherUpdateInfo update,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(LauncherSelfUpdateStartResult.Failed());
        }
    }

    private sealed class NullApplicationExitService : IApplicationExitService
    {
        public static readonly NullApplicationExitService Instance = new();

        private NullApplicationExitService()
        {
        }

        public void Shutdown()
        {
        }
    }
}
