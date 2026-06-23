using System.ComponentModel;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.App.Utilities;
using Launcher.App.ViewModels.Settings;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDetailsViewModel : ObservableObject
{
    private readonly GameSettingsEditDialogViewModel editDialog;
    private readonly IGameInstanceService instanceService;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private readonly ISystemMemoryService systemMemoryService;
    private readonly IModService modService;
    private INotifyPropertyChanged? selectedInstanceNotifier;
    private LauncherSettings globalSettings = new();
    private CancellationTokenSource? descriptionSaveCancellationTokenSource;
    private bool suppressInstanceSettingsAutoSave;
    private int enabledModCount;

    [ObservableProperty]
    private GameSettingsInstanceItem? selectedInstance;

    [ObservableProperty]
    private GameSettingsDetailSectionItem? selectedSection;

    [ObservableProperty]
    private GameSettingsDetailsSectionViewModelBase? currentSectionViewModel;

    [ObservableProperty]
    private string descriptionText = string.Empty;

    [ObservableProperty]
    private bool launchCheckFilesBeforeLaunchEnabled;

    [ObservableProperty]
    private bool launchAutoRepairMissingFilesEnabled;

    [ObservableProperty]
    private bool launchMinimizeLauncherAfterLaunchEnabled;

    [ObservableProperty]
    private bool launchFullScreenEnabled;

    [ObservableProperty]
    private string launchPreLaunchCommand = string.Empty;

    [ObservableProperty]
    private bool launchWaitForPreLaunchCommand = true;

    [ObservableProperty]
    private string launchPostExitCommand = string.Empty;

    [ObservableProperty]
    private string launchJvmArguments = string.Empty;

    [ObservableProperty]
    private string launchGameArguments = string.Empty;

    [ObservableProperty]
    private SettingsMemoryModeOption? selectedMemoryModeOption;

    [ObservableProperty]
    private double memoryMb = LauncherDefaults.DefaultMemoryMb;

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
    private GameSettingsLaunchSettingsModeOption? selectedLaunchSettingsModeOption;

    [ObservableProperty]
    private GameSettingsLaunchSettingsModeOption? selectedInstanceJavaSettingsModeOption;

    public GameSettingsDetailsViewModel(
        GameSettingsEditDialogViewModel editDialog,
        IGameInstanceService instanceService,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService,
        ISystemMemoryService systemMemoryService,
        IModService modService,
        LocalModsViewModel localModsViewModel,
        LocalSavesViewModel localSavesViewModel,
        LocalResourcePacksViewModel localResourcePacksViewModel,
        LocalShaderPacksViewModel localShaderPacksViewModel,
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService)
    {
        this.editDialog = editDialog;
        this.instanceService = instanceService;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
        this.systemMemoryService = systemMemoryService;
        this.modService = modService;
        InstanceJavaSettings = new JavaSettingsEditorViewModel(
            javaRuntimeDiscoveryService,
            statusService,
            filePickerService,
            floatingMessageService,
            () => globalSettings.MinecraftDirectory);
        InstanceJavaSettings.IsEditorEnabled = false;
        InstanceJavaSettings.PropertyChanged += InstanceJavaSettings_PropertyChanged;
        InstanceJavaSettings.JavaSelectionChanged += InstanceJavaSettings_JavaSelectionChanged;
        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Auto,
            Strings.Settings_MemoryModeAuto));
        MemoryModeOptions.Add(new SettingsMemoryModeOption(
            MemorySettingsMode.Manual,
            Strings.Settings_MemoryModeManual));
        SelectedMemoryModeOption = MemoryModeOptions[0];
        SelectedInstanceJavaSettingsModeOption = LaunchSettingsModeOptions[0];
        General = new InstanceGeneralSettingsViewModel(this);
        Launch = new InstanceLaunchSettingsViewModel(this);
        Java = new InstanceJavaSettingsViewModel(this);
        ModManagement = new InstanceModManagementSettingsViewModel(
            this,
            localModsViewModel,
            statusService,
            instanceFolderService,
            filePickerService);
        ModManagement.DeleteModsRequested += ModManagement_DeleteModsRequested;
        ModManagement.ImportModConflictRequested += ModManagement_ImportModConflictRequested;
        SaveManagement = new InstanceSaveManagementSettingsViewModel(
            this,
            localSavesViewModel,
            statusService,
            instanceFolderService,
            filePickerService);
        SaveManagement.DeleteSavesRequested += SaveManagement_DeleteSavesRequested;
        SaveManagement.SaveImportFailedRequested += SaveManagement_SaveImportFailedRequested;
        ResourcePackManagement = new InstanceResourcePackManagementSettingsViewModel(
            this,
            localResourcePacksViewModel,
            statusService,
            instanceFolderService,
            filePickerService);
        ResourcePackManagement.DeleteResourcePacksRequested += ResourcePackManagement_DeleteResourcePacksRequested;
        ResourcePackManagement.ResourcePackImportFailedRequested += ResourcePackManagement_ResourcePackImportFailedRequested;
        ShaderPackManagement = new InstanceShaderPackManagementSettingsViewModel(
            this,
            localShaderPacksViewModel,
            statusService,
            instanceFolderService,
            filePickerService);
        ShaderPackManagement.DeleteShaderPacksRequested += ShaderPackManagement_DeleteShaderPacksRequested;
        ShaderPackManagement.ShaderPackImportFailedRequested += ShaderPackManagement_ShaderPackImportFailedRequested;
        Placeholder = new InstancePlaceholderSettingsViewModel(this);
        CurrentSectionViewModel = General;
    }

    public event Action<GameInstance>? InstanceSettingsSaved;

    public event Action<GameSettingsInstanceItem>? DeleteInstanceRequested;

    public event Action<ModDeleteRequest>? DeleteModsRequested;
    public event Action<SaveDeleteRequest>? DeleteSavesRequested;
    public event Action<ModImportConflictRequest>? ImportModConflictRequested;
    public event Action<SaveImportFailureRequest>? SaveImportFailedRequested;
    public event Action<ResourcePackDeleteRequest>? DeleteResourcePacksRequested;
    public event Action<ResourcePackImportFailureRequest>? ResourcePackImportFailedRequested;
    public event Action<ShaderPackDeleteRequest>? DeleteShaderPacksRequested;
    public event Action<ShaderPackImportFailureRequest>? ShaderPackImportFailedRequested;

    public bool HasSelectedInstance => SelectedInstance is not null;

    public string InstanceName => SelectedInstance?.Name ?? string.Empty;

    public string InstanceIconSource => SelectedInstance?.IconSource ?? string.Empty;

    public string InstanceSubtitle => SelectedInstance?.Subtitle ?? string.Empty;

    public string SectionTitle => SelectedSection?.Title ?? Strings.GameSettings_DetailGeneral;

    public string SectionPlaceholderBody => string.Format(
        Strings.GameSettings_DetailPlaceholderBodyFormat,
        SectionTitle);

    public GameSettingsDetailsSectionViewModelBase? ScrollSectionViewModel =>
        CurrentSectionViewModel?.UsesFullViewportLayout is true ? null : CurrentSectionViewModel;

    public GameSettingsDetailsSectionViewModelBase? FullViewportSectionViewModel =>
        CurrentSectionViewModel?.UsesFullViewportLayout is true ? CurrentSectionViewModel : null;

    public bool IsGeneralSection => string.Equals(SelectedSection?.Id, "general", StringComparison.OrdinalIgnoreCase);

    public bool IsLaunchSection => string.Equals(SelectedSection?.Id, "launch", StringComparison.OrdinalIgnoreCase);

    public bool IsJavaSection => string.Equals(SelectedSection?.Id, "java", StringComparison.OrdinalIgnoreCase);

    public bool AreLaunchSettingsOverridesEnabled => SelectedLaunchSettingsModeOption?.Mode is LaunchSettingsMode.PerInstance;

    public bool AreInstanceJavaSettingsOverridesEnabled => SelectedInstanceJavaSettingsModeOption?.Mode is LaunchSettingsMode.PerInstance;

    public bool IsInstanceJavaManualSelection => InstanceJavaSettings.IsJavaManualSelection;

    public bool CanInteractWithInstanceJavaRuntimeList => AreInstanceJavaSettingsOverridesEnabled && IsInstanceJavaManualSelection;

    public bool CanEditAutoRepairMissingFiles => AreLaunchSettingsOverridesEnabled && LaunchCheckFilesBeforeLaunchEnabled;

    public bool IsMemorySliderEnabled => AreLaunchSettingsOverridesEnabled && SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public bool IsMemorySliderVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Manual;

    public bool IsAutomaticMemorySummaryVisible => SelectedMemoryModeOption?.Mode is MemorySettingsMode.Auto;

    public string MemoryText => MemorySizeTextFormatter.FormatGb(MemoryMb);

    public string AutomaticMemoryText => MemorySizeTextFormatter.FormatGb(AutomaticMemoryMb);

    public string SystemMemorySummaryText => string.Format(
        Strings.Settings_SystemMemorySummaryFormat,
        SystemAvailableMemoryText,
        SystemTotalMemoryText);

    public string InstanceCreatedAtText => SelectedInstance?.Instance.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

    public IReadOnlyList<GameSettingsLaunchSettingsModeOption> LaunchSettingsModeOptions { get; } =
    [
        new(LaunchSettingsMode.UseGlobal, Strings.GameSettings_LaunchSettingsModeUseGlobal),
        new(LaunchSettingsMode.PerInstance, Strings.GameSettings_LaunchSettingsModePerInstance)
    ];

    public ObservableCollection<SettingsMemoryModeOption> MemoryModeOptions { get; } = [];

    public JavaSettingsEditorViewModel InstanceJavaSettings { get; }

    public InstanceGeneralSettingsViewModel General { get; }

    public InstanceLaunchSettingsViewModel Launch { get; }

    public InstanceJavaSettingsViewModel Java { get; }

    public InstanceModManagementSettingsViewModel ModManagement { get; }

    public InstanceSaveManagementSettingsViewModel SaveManagement { get; }

    public InstanceResourcePackManagementSettingsViewModel ResourcePackManagement { get; }

    public InstanceShaderPackManagementSettingsViewModel ShaderPackManagement { get; }

    public InstancePlaceholderSettingsViewModel Placeholder { get; }

    public ObservableCollection<SettingsJavaSelectionOption> InstanceJavaSelectionOptions => InstanceJavaSettings.JavaSelectionOptions;

    public ObservableCollection<SettingsJavaRuntimeItem> InstanceJavaRuntimes => InstanceJavaSettings.JavaRuntimes;

    public SettingsJavaSelectionOption? SelectedInstanceJavaSelectionOption
    {
        get => InstanceJavaSettings.SelectedJavaSelectionOption;
        set => InstanceJavaSettings.SelectedJavaSelectionOption = value;
    }

    public SettingsJavaRuntimeItem? SelectedInstanceJavaRuntime
    {
        get => InstanceJavaSettings.SelectedJavaRuntime;
        set => InstanceJavaSettings.SelectedJavaRuntime = value;
    }

    public bool IsInstanceJavaRuntimeScanRunning => InstanceJavaSettings.IsJavaRuntimeScanRunning;

    public string InstanceJavaRuntimeListMessage => InstanceJavaSettings.JavaRuntimeListMessage;

    public bool HasInstanceJavaRuntimeListMessage => InstanceJavaSettings.HasJavaRuntimeListMessage;

    public IAsyncRelayCommand RefreshInstanceJavaRuntimesCommand => InstanceJavaSettings.RefreshJavaRuntimesCommand;

    public IAsyncRelayCommand ImportInstanceJavaRuntimeCommand => InstanceJavaSettings.ImportJavaRuntimeCommand;

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        globalSettings = launcherSettings;
        RefreshSystemMemorySnapshot();

        if (SelectedLaunchSettingsModeOption?.Mode is LaunchSettingsMode.UseGlobal)
            ApplyGlobalLaunchSettingsToEditor();

        if (SelectedInstanceJavaSettingsModeOption?.Mode is LaunchSettingsMode.UseGlobal)
        {
            suppressInstanceSettingsAutoSave = true;
            try
            {
                ApplyJavaSettingsToEditor();
            }
            finally
            {
                suppressInstanceSettingsAutoSave = false;
            }

            _ = InstanceJavaSettings.RefreshJavaRuntimesForDisplayAsync();
        }
    }

    public void SetSelectedInstance(GameSettingsInstanceItem? instance)
    {
        SelectedInstance = instance;
    }

    public void SetSelectedSection(GameSettingsDetailSectionItem? section)
    {
        SelectedSection = section;
    }

    public Task DeleteModsAsync(IReadOnlyList<string> fullPaths)
    {
        return ModManagement.DeleteModsAsync(fullPaths);
    }

    public Task DeleteSavesAsync(IReadOnlyList<string> fullPaths)
    {
        return SaveManagement.DeleteSavesAsync(fullPaths);
    }

    public Task DeleteResourcePacksAsync(IReadOnlyList<string> fullPaths)
    {
        return ResourcePackManagement.DeleteResourcePacksAsync(fullPaths);
    }

    public Task DeleteShaderPacksAsync(IReadOnlyList<string> fullPaths)
    {
        return ShaderPackManagement.DeleteShaderPacksAsync(fullPaths);
    }

    public GameSettingsFileDropEvaluation EvaluateImportDrop(IReadOnlyList<string> paths)
    {
        if (SelectedInstance is null)
            return GameSettingsFileDropEvaluation.Hidden;

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.EvaluateDroppedFiles(paths),
            "saves" => SaveManagement.EvaluateDroppedFiles(paths),
            "resource_packs" => ResourcePackManagement.EvaluateDroppedFiles(paths),
            "shaders" => ShaderPackManagement.EvaluateDroppedFiles(paths),
            _ => GameSettingsFileDropEvaluation.Hidden
        };
    }

    public Task HandleImportDropAsync(IReadOnlyList<string> paths)
    {
        if (SelectedInstance is null)
            return Task.CompletedTask;

        return SelectedSection?.Id?.ToLowerInvariant() switch
        {
            "mod_management" => ModManagement.ImportDroppedModFilesAsync(paths),
            "saves" => SaveManagement.ImportDroppedSaveArchivesAsync(paths),
            "resource_packs" => ResourcePackManagement.ImportDroppedResourcePackArchivesAsync(paths),
            "shaders" => ShaderPackManagement.ImportDroppedShaderPackArchivesAsync(paths),
            _ => Task.CompletedTask
        };
    }

    public void ResolvePendingModImportConflict(bool shouldReplace)
    {
        if (shouldReplace)
            ModManagement.ReplaceImportedModAsync(string.Empty);
        else
            ModManagement.SkipPendingImportedModReplacement();
    }

    [RelayCommand]
    private void RequestEditInstance()
    {
        if (SelectedInstance is null)
            return;

        editDialog.Open(SelectedInstance);
    }

    [RelayCommand]
    private void OpenInstanceDirectory()
    {
        if (SelectedInstance is null)
            return;

        var folderPath = SelectedInstance.Instance.InstanceDirectory;
        if (!instanceFolderService.DirectoryExists(folderPath))
        {
            statusService.Report(Strings.Status_InstanceFolderNotFound);
            return;
        }

        if (!instanceFolderService.TryOpen(folderPath))
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
    }

    [RelayCommand]
    private void RequestDeleteInstance()
    {
        if (SelectedInstance is null)
            return;

        DeleteInstanceRequested?.Invoke(SelectedInstance);
    }

    partial void OnSelectedInstanceChanged(GameSettingsInstanceItem? value)
    {
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged -= SelectedInstance_PropertyChanged;

        selectedInstanceNotifier = value;
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged += SelectedInstance_PropertyChanged;

        enabledModCount = 0;
        OnPropertyChanged(nameof(HasSelectedInstance));
        OnPropertyChanged(nameof(InstanceName));
        OnPropertyChanged(nameof(InstanceIconSource));
        OnPropertyChanged(nameof(InstanceSubtitle));
        OnPropertyChanged(nameof(InstanceCreatedAtText));
        CancelPendingDescriptionSave();
        suppressInstanceSettingsAutoSave = true;
        try
        {
            DescriptionText = value?.Instance.Description ?? string.Empty;
            var mode = value?.Instance.LaunchSettingsMode ?? LaunchSettingsMode.UseGlobal;
            SelectedLaunchSettingsModeOption = ResolveLaunchSettingsModeOption(mode);
            var javaSettingsMode = value?.Instance.JavaSettingsMode ?? LaunchSettingsMode.UseGlobal;
            SelectedInstanceJavaSettingsModeOption = ResolveLaunchSettingsModeOption(javaSettingsMode);
            ApplyJavaSettingsToEditor();
            ModManagement.OnSelectedInstanceChanged(value?.Instance);
            SaveManagement.OnSelectedInstanceChanged(value?.Instance);
            ResourcePackManagement.OnSelectedInstanceChanged(value?.Instance);
            ShaderPackManagement.OnSelectedInstanceChanged(value?.Instance);
            _ = RefreshEnabledModCountAsync(value?.Instance);

            if (mode is LaunchSettingsMode.UseGlobal)
            {
                ApplyGlobalLaunchSettingsToEditor();
            }
            else
            {
                SelectedMemoryModeOption = ResolveMemoryModeOption(value?.Instance.MemorySettingsMode ?? MemorySettingsMode.Manual);
                MemoryMb = NormalizeMemoryValue(value?.Instance.MemoryMb ?? LauncherDefaults.DefaultMemoryMb);
                LaunchCheckFilesBeforeLaunchEnabled = value?.Instance.CheckFilesBeforeLaunch ?? true;
                LaunchAutoRepairMissingFilesEnabled = value?.Instance.AutoRepairMissingFiles ?? true;
                LaunchMinimizeLauncherAfterLaunchEnabled = value?.Instance.MinimizeLauncherAfterLaunch ?? false;
                LaunchFullScreenEnabled = value?.Instance.LaunchFullScreen ?? false;
                LaunchPreLaunchCommand = value?.Instance.PreLaunchCommand ?? string.Empty;
                LaunchWaitForPreLaunchCommand = value?.Instance.WaitForPreLaunchCommand ?? true;
                LaunchPostExitCommand = value?.Instance.PostExitCommand ?? string.Empty;
                LaunchJvmArguments = value?.Instance.JvmArguments ?? string.Empty;
                LaunchGameArguments = value?.Instance.GameArguments ?? string.Empty;
            }
        }
        finally
        {
            suppressInstanceSettingsAutoSave = false;
        }

        RefreshSystemMemorySnapshot();
        if (IsJavaSection || InstanceJavaRuntimes.Count == 0)
            _ = InstanceJavaSettings.RefreshJavaRuntimesForDisplayAsync();

        if (CurrentSectionViewModel is not null)
            _ = CurrentSectionViewModel.OnSectionActivatedAsync();
    }

    partial void OnSelectedSectionChanged(GameSettingsDetailSectionItem? value)
    {
        var previousSectionViewModel = CurrentSectionViewModel;
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsLaunchSection));
        OnPropertyChanged(nameof(IsJavaSection));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionPlaceholderBody));
        previousSectionViewModel?.OnSectionDeactivated();
        CurrentSectionViewModel = value?.Id?.ToLowerInvariant() switch
        {
            "general" => General,
            "launch" => Launch,
            "java" => Java,
            "mod_management" => ModManagement,
            "saves" => SaveManagement,
            "resource_packs" => ResourcePackManagement,
            "shaders" => ShaderPackManagement,
            _ => Placeholder
        };
        if (CurrentSectionViewModel is not null)
            _ = CurrentSectionViewModel.OnSectionActivatedAsync();
    }

    partial void OnCurrentSectionViewModelChanged(GameSettingsDetailsSectionViewModelBase? value)
    {
        OnPropertyChanged(nameof(ScrollSectionViewModel));
        OnPropertyChanged(nameof(FullViewportSectionViewModel));
    }

    partial void OnSelectedLaunchSettingsModeOptionChanged(GameSettingsLaunchSettingsModeOption? value)
    {
        OnPropertyChanged(nameof(AreLaunchSettingsOverridesEnabled));
        OnPropertyChanged(nameof(CanEditAutoRepairMissingFiles));
        OnPropertyChanged(nameof(IsMemorySliderEnabled));

        if (suppressInstanceSettingsAutoSave)
            return;

        if (value?.Mode is LaunchSettingsMode.UseGlobal)
            ApplyGlobalLaunchSettingsToEditor();

        SaveLaunchSettings();
    }

    partial void OnSelectedInstanceJavaSettingsModeOptionChanged(GameSettingsLaunchSettingsModeOption? value)
    {
        OnPropertyChanged(nameof(AreInstanceJavaSettingsOverridesEnabled));
        OnPropertyChanged(nameof(CanInteractWithInstanceJavaRuntimeList));

        if (suppressInstanceSettingsAutoSave)
            return;

        suppressInstanceSettingsAutoSave = true;
        try
        {
            ApplyJavaSettingsToEditor();
        }
        finally
        {
            suppressInstanceSettingsAutoSave = false;
        }

        SaveJavaSettings();

        _ = InstanceJavaSettings.RefreshJavaRuntimesForDisplayAsync();
    }

    partial void OnDescriptionTextChanged(string value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        ScheduleDescriptionSave();
    }

    partial void OnLaunchCheckFilesBeforeLaunchEnabledChanged(bool value)
    {
        if (!suppressInstanceSettingsAutoSave)
            ApplyLaunchCheckDependency(value, synchronizeAutoRepair: true);

        OnPropertyChanged(nameof(CanEditAutoRepairMissingFiles));

        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchAutoRepairMissingFilesEnabledChanged(bool value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchMinimizeLauncherAfterLaunchEnabledChanged(bool value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchFullScreenEnabledChanged(bool value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchPreLaunchCommandChanged(string value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchWaitForPreLaunchCommandChanged(bool value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchPostExitCommandChanged(string value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchJvmArgumentsChanged(string value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnLaunchGameArgumentsChanged(string value)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnSelectedMemoryModeOptionChanged(SettingsMemoryModeOption? value)
    {
        OnPropertyChanged(nameof(IsMemorySliderEnabled));
        OnPropertyChanged(nameof(IsMemorySliderVisible));
        OnPropertyChanged(nameof(IsAutomaticMemorySummaryVisible));

        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnMemoryMbChanged(double value)
    {
        var clamped = Math.Clamp(value, MemorySliderMinimumMb, MemorySliderMaximumMb);
        if (Math.Abs(clamped - value) > double.Epsilon)
        {
            MemoryMb = clamped;
            return;
        }

        OnPropertyChanged(nameof(MemoryText));

        if (suppressInstanceSettingsAutoSave)
            return;

        SaveLaunchSettings();
    }

    partial void OnMemorySliderMaximumMbChanged(int value)
    {
        if (MemoryMb > value)
            MemoryMb = value;
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

    private void SelectedInstance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(InstanceName));
        OnPropertyChanged(nameof(InstanceIconSource));
        OnPropertyChanged(nameof(InstanceSubtitle));
        OnPropertyChanged(nameof(InstanceCreatedAtText));
    }

    private void InstanceJavaSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(JavaSettingsEditorViewModel.SelectedJavaSelectionOption):
                OnPropertyChanged(nameof(SelectedInstanceJavaSelectionOption));
                break;
            case nameof(JavaSettingsEditorViewModel.SelectedJavaRuntime):
                OnPropertyChanged(nameof(SelectedInstanceJavaRuntime));
                break;
            case nameof(JavaSettingsEditorViewModel.IsJavaRuntimeScanRunning):
                OnPropertyChanged(nameof(IsInstanceJavaRuntimeScanRunning));
                break;
            case nameof(JavaSettingsEditorViewModel.JavaRuntimeListMessage):
                OnPropertyChanged(nameof(InstanceJavaRuntimeListMessage));
                OnPropertyChanged(nameof(HasInstanceJavaRuntimeListMessage));
                break;
            case nameof(JavaSettingsEditorViewModel.HasJavaRuntimeListMessage):
                OnPropertyChanged(nameof(HasInstanceJavaRuntimeListMessage));
                break;
            case nameof(JavaSettingsEditorViewModel.IsJavaManualSelection):
                OnPropertyChanged(nameof(IsInstanceJavaManualSelection));
                OnPropertyChanged(nameof(CanInteractWithInstanceJavaRuntimeList));
                break;
        }
    }

    private void InstanceJavaSettings_JavaSelectionChanged(object? sender, EventArgs e)
    {
        if (suppressInstanceSettingsAutoSave)
            return;

        SaveJavaSettings();
    }

    private static string NormalizeDescription(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private void ApplyGlobalLaunchSettingsToEditor()
    {
        suppressInstanceSettingsAutoSave = true;
        try
        {
            LaunchCheckFilesBeforeLaunchEnabled = globalSettings.DefaultCheckFilesBeforeLaunch;
            LaunchAutoRepairMissingFilesEnabled = globalSettings.DefaultAutoRepairMissingFiles;
            LaunchMinimizeLauncherAfterLaunchEnabled = globalSettings.DefaultMinimizeLauncherAfterLaunch;
            LaunchFullScreenEnabled = globalSettings.DefaultLaunchFullScreen;
            LaunchPreLaunchCommand = globalSettings.DefaultPreLaunchCommand;
            LaunchWaitForPreLaunchCommand = globalSettings.DefaultWaitForPreLaunchCommand;
            LaunchPostExitCommand = globalSettings.DefaultPostExitCommand;
            LaunchJvmArguments = globalSettings.DefaultJvmArguments;
            LaunchGameArguments = globalSettings.DefaultGameArguments;
            SelectedMemoryModeOption = ResolveMemoryModeOption(globalSettings.DefaultMemorySettingsMode);
            MemoryMb = NormalizeMemoryValue(globalSettings.DefaultMemoryMb);
        }
        finally
        {
            suppressInstanceSettingsAutoSave = false;
        }
    }

    private void ApplyLaunchCheckDependency(bool checkFilesBeforeLaunch, bool synchronizeAutoRepair)
    {
        if (!synchronizeAutoRepair)
            return;

        var targetAutoRepairValue = checkFilesBeforeLaunch;
        if (LaunchAutoRepairMissingFilesEnabled == targetAutoRepairValue)
            return;

        suppressInstanceSettingsAutoSave = true;
        try
        {
            LaunchAutoRepairMissingFilesEnabled = targetAutoRepairValue;
        }
        finally
        {
            suppressInstanceSettingsAutoSave = false;
        }
    }

    private void SaveLaunchSettings()
    {
        if (SelectedInstance is null)
            return;

        var instanceId = SelectedInstance.Instance.Id;
        var mode = SelectedLaunchSettingsModeOption?.Mode ?? LaunchSettingsMode.UseGlobal;
        var checkFilesBeforeLaunch = LaunchCheckFilesBeforeLaunchEnabled;
        var autoRepairMissingFiles = LaunchAutoRepairMissingFilesEnabled;
        var minimizeLauncherAfterLaunch = LaunchMinimizeLauncherAfterLaunchEnabled;
        var launchFullScreen = LaunchFullScreenEnabled;
        var preLaunchCommand = NormalizeSettingText(LaunchPreLaunchCommand);
        var waitForPreLaunchCommand = LaunchWaitForPreLaunchCommand;
        var postExitCommand = NormalizeSettingText(LaunchPostExitCommand);
        var jvmArguments = NormalizeSettingText(LaunchJvmArguments);
        var gameArguments = NormalizeSettingText(LaunchGameArguments);
        var memorySettingsMode = mode is LaunchSettingsMode.UseGlobal
            ? globalSettings.DefaultMemorySettingsMode
            : SelectedMemoryModeOption?.Mode ?? MemorySettingsMode.Manual;
        var memoryMb = mode is LaunchSettingsMode.UseGlobal
            ? NormalizeMemoryValue(globalSettings.DefaultMemoryMb)
            : NormalizeMemoryValue(MemoryMb);
        _ = SaveLaunchSettingsAsync(
            instanceId,
            mode,
            checkFilesBeforeLaunch,
            autoRepairMissingFiles,
            minimizeLauncherAfterLaunch,
            launchFullScreen,
            preLaunchCommand,
            waitForPreLaunchCommand,
            postExitCommand,
            jvmArguments,
            gameArguments,
            memorySettingsMode,
            memoryMb);
    }

    private void SaveJavaSettings()
    {
        if (SelectedInstance is null)
            return;

        var instanceId = SelectedInstance.Instance.Id;
        var javaSettingsMode = SelectedInstanceJavaSettingsModeOption?.Mode ?? LaunchSettingsMode.UseGlobal;
        var javaSelectionMode = javaSettingsMode is LaunchSettingsMode.UseGlobal
            ? SelectedInstance.Instance.JavaSelectionMode
            : InstanceJavaSettings.SelectedMode;
        var selectedJavaExecutablePath = javaSettingsMode is LaunchSettingsMode.UseGlobal
            ? SelectedInstance.Instance.SelectedJavaExecutablePath
            : InstanceJavaSettings.SelectedExecutablePath;

        _ = SaveJavaSettingsAsync(
            instanceId,
            javaSettingsMode,
            javaSelectionMode,
            selectedJavaExecutablePath);
    }

    private void ScheduleDescriptionSave()
    {
        CancelPendingDescriptionSave();

        if (SelectedInstance is null)
            return;

        var instanceId = SelectedInstance.Instance.Id;
        var cancellationTokenSource = new CancellationTokenSource();
        descriptionSaveCancellationTokenSource = cancellationTokenSource;
        _ = SaveDescriptionAfterDelayAsync(instanceId, cancellationTokenSource.Token);
    }

    private async Task SaveDescriptionAfterDelayAsync(string instanceId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(450, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (SelectedInstance is null
            || !string.Equals(SelectedInstance.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var instance = SelectedInstance.Instance;
        var normalizedDescription = NormalizeDescription(DescriptionText);
        if (string.Equals(instance.Description, normalizedDescription, StringComparison.Ordinal))
            return;

        var originalDescription = instance.Description;
        try
        {
            instance.Description = normalizedDescription;
            await instanceService.SaveInstanceAsync(instance, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            instance.Description = originalDescription;
        }
        catch (Exception)
        {
            instance.Description = originalDescription;
            statusService.Report(Strings.Status_InstanceSettingsSaveFailed);
        }
    }

    private async Task SaveLaunchSettingsAsync(
        string instanceId,
        LaunchSettingsMode mode,
        bool checkFilesBeforeLaunch,
        bool autoRepairMissingFiles,
        bool minimizeLauncherAfterLaunch,
        bool launchFullScreen,
        string preLaunchCommand,
        bool waitForPreLaunchCommand,
        string postExitCommand,
        string jvmArguments,
        string gameArguments,
        MemorySettingsMode memorySettingsMode,
        int memoryMb)
    {
        if (SelectedInstance is null
            || !string.Equals(SelectedInstance.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var instance = SelectedInstance.Instance;
        var originalMode = instance.LaunchSettingsMode;
        var originalCheckFilesBeforeLaunch = instance.CheckFilesBeforeLaunch;
        var originalAutoRepairMissingFiles = instance.AutoRepairMissingFiles;
        var originalMinimizeLauncherAfterLaunch = instance.MinimizeLauncherAfterLaunch;
        var originalLaunchFullScreen = instance.LaunchFullScreen;
        var originalPreLaunchCommand = instance.PreLaunchCommand;
        var originalWaitForPreLaunchCommand = instance.WaitForPreLaunchCommand;
        var originalPostExitCommand = instance.PostExitCommand;
        var originalJvmArguments = instance.JvmArguments;
        var originalGameArguments = instance.GameArguments;
        var originalMemorySettingsMode = instance.MemorySettingsMode;
        var originalMemoryMb = instance.MemoryMb;

        if (originalMode == mode
            && originalCheckFilesBeforeLaunch == checkFilesBeforeLaunch
            && originalAutoRepairMissingFiles == autoRepairMissingFiles
            && originalMinimizeLauncherAfterLaunch == minimizeLauncherAfterLaunch
            && originalLaunchFullScreen == launchFullScreen
            && string.Equals(originalPreLaunchCommand, preLaunchCommand, StringComparison.Ordinal)
            && originalWaitForPreLaunchCommand == waitForPreLaunchCommand
            && string.Equals(originalPostExitCommand, postExitCommand, StringComparison.Ordinal)
            && string.Equals(originalJvmArguments, jvmArguments, StringComparison.Ordinal)
            && string.Equals(originalGameArguments, gameArguments, StringComparison.Ordinal)
            && originalMemorySettingsMode == memorySettingsMode
            && originalMemoryMb == memoryMb)
        {
            return;
        }

        try
        {
            instance.LaunchSettingsMode = mode;
            instance.CheckFilesBeforeLaunch = checkFilesBeforeLaunch;
            instance.AutoRepairMissingFiles = autoRepairMissingFiles;
            instance.MinimizeLauncherAfterLaunch = minimizeLauncherAfterLaunch;
            instance.LaunchFullScreen = launchFullScreen;
            instance.PreLaunchCommand = preLaunchCommand;
            instance.WaitForPreLaunchCommand = waitForPreLaunchCommand;
            instance.PostExitCommand = postExitCommand;
            instance.JvmArguments = jvmArguments;
            instance.GameArguments = gameArguments;
            instance.MemorySettingsMode = memorySettingsMode;
            instance.MemoryMb = memoryMb;
            await instanceService.SaveInstanceAsync(instance);
            InstanceSettingsSaved?.Invoke(instance);
        }
        catch (Exception)
        {
            instance.LaunchSettingsMode = originalMode;
            instance.CheckFilesBeforeLaunch = originalCheckFilesBeforeLaunch;
            instance.AutoRepairMissingFiles = originalAutoRepairMissingFiles;
            instance.MinimizeLauncherAfterLaunch = originalMinimizeLauncherAfterLaunch;
            instance.LaunchFullScreen = originalLaunchFullScreen;
            instance.PreLaunchCommand = originalPreLaunchCommand;
            instance.WaitForPreLaunchCommand = originalWaitForPreLaunchCommand;
            instance.PostExitCommand = originalPostExitCommand;
            instance.JvmArguments = originalJvmArguments;
            instance.GameArguments = originalGameArguments;
            instance.MemorySettingsMode = originalMemorySettingsMode;
            instance.MemoryMb = originalMemoryMb;

            suppressInstanceSettingsAutoSave = true;
            SelectedLaunchSettingsModeOption = ResolveLaunchSettingsModeOption(originalMode);
            SelectedMemoryModeOption = ResolveMemoryModeOption(originalMemorySettingsMode);
            MemoryMb = NormalizeMemoryValue(originalMemoryMb);
            LaunchCheckFilesBeforeLaunchEnabled = originalCheckFilesBeforeLaunch;
            LaunchAutoRepairMissingFilesEnabled = originalAutoRepairMissingFiles;
            LaunchMinimizeLauncherAfterLaunchEnabled = originalMinimizeLauncherAfterLaunch;
            LaunchFullScreenEnabled = originalLaunchFullScreen;
            LaunchPreLaunchCommand = originalPreLaunchCommand;
            LaunchWaitForPreLaunchCommand = originalWaitForPreLaunchCommand;
            LaunchPostExitCommand = originalPostExitCommand;
            LaunchJvmArguments = originalJvmArguments;
            LaunchGameArguments = originalGameArguments;
            suppressInstanceSettingsAutoSave = false;

            statusService.Report(Strings.Status_InstanceSettingsSaveFailed);
        }
    }

    private async Task SaveJavaSettingsAsync(
        string instanceId,
        LaunchSettingsMode javaSettingsMode,
        JavaSelectionMode javaSelectionMode,
        string? selectedJavaExecutablePath)
    {
        if (SelectedInstance is null
            || !string.Equals(SelectedInstance.Instance.Id, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        selectedJavaExecutablePath = string.IsNullOrWhiteSpace(selectedJavaExecutablePath)
            ? null
            : selectedJavaExecutablePath;

        var instance = SelectedInstance.Instance;
        var originalJavaSettingsMode = instance.JavaSettingsMode;
        var originalJavaSelectionMode = instance.JavaSelectionMode;
        var originalSelectedJavaExecutablePath = instance.SelectedJavaExecutablePath;

        if (originalJavaSettingsMode == javaSettingsMode
            && originalJavaSelectionMode == javaSelectionMode
            && string.Equals(originalSelectedJavaExecutablePath, selectedJavaExecutablePath, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            instance.JavaSettingsMode = javaSettingsMode;
            instance.JavaSelectionMode = javaSelectionMode;
            instance.SelectedJavaExecutablePath = selectedJavaExecutablePath;
            await instanceService.SaveInstanceAsync(instance);
            InstanceSettingsSaved?.Invoke(instance);
        }
        catch (Exception)
        {
            instance.JavaSettingsMode = originalJavaSettingsMode;
            instance.JavaSelectionMode = originalJavaSelectionMode;
            instance.SelectedJavaExecutablePath = originalSelectedJavaExecutablePath;

            suppressInstanceSettingsAutoSave = true;
            try
            {
                SelectedInstanceJavaSettingsModeOption = ResolveLaunchSettingsModeOption(originalJavaSettingsMode);
                InstanceJavaSettings.IsEditorEnabled = originalJavaSettingsMode is LaunchSettingsMode.PerInstance;
                InstanceJavaSettings.LoadSelection(originalJavaSelectionMode, originalSelectedJavaExecutablePath);
            }
            finally
            {
                suppressInstanceSettingsAutoSave = false;
            }

            statusService.Report(Strings.Status_InstanceSettingsSaveFailed);
        }
    }

    private void ApplyJavaSettingsToEditor()
    {
        var instance = SelectedInstance?.Instance;
        var usePerInstanceJavaSettings = SelectedInstanceJavaSettingsModeOption?.Mode is LaunchSettingsMode.PerInstance;
        var javaSelectionMode = usePerInstanceJavaSettings
            ? instance?.JavaSelectionMode ?? JavaSelectionMode.Auto
            : globalSettings.JavaSelectionMode;
        var selectedJavaExecutablePath = usePerInstanceJavaSettings
            ? instance?.SelectedJavaExecutablePath
            : globalSettings.SelectedJavaExecutablePath;

        InstanceJavaSettings.IsEditorEnabled = usePerInstanceJavaSettings;
        InstanceJavaSettings.LoadSelection(javaSelectionMode, selectedJavaExecutablePath);
        OnPropertyChanged(nameof(IsInstanceJavaManualSelection));
        OnPropertyChanged(nameof(CanInteractWithInstanceJavaRuntimeList));
    }

    public void RefreshSystemMemorySnapshot()
    {
        try
        {
            var snapshot = systemMemoryService.GetSnapshot();
            var totalMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.TotalMemoryBytes);
            var availableMemoryMb = MemoryAllocationCalculator.BytesToMegabytes(snapshot.AvailableMemoryBytes);
            MemorySliderMaximumMb = MemoryAllocationCalculator.CalculateMaximumMemoryMb(totalMemoryMb);
            AutomaticMemoryMb = MemoryAllocationCalculator.CalculateAutomaticMemoryMb(
                snapshot,
                SelectedInstance?.Instance.Loader ?? LoaderKind.Vanilla,
                enabledModCount);
            SystemTotalMemoryText = MemorySizeTextFormatter.Format(totalMemoryMb);
            SystemAvailableMemoryText = MemorySizeTextFormatter.FormatGb(availableMemoryMb);
        }
        catch (Exception)
        {
            MemorySliderMaximumMb = MemoryAllocationCalculator.FallbackMaximumMemoryMb;
            AutomaticMemoryMb = NormalizeMemoryValue(MemoryMb);
            SystemTotalMemoryText = Strings.Settings_MemoryUnavailable;
            SystemAvailableMemoryText = Strings.Settings_MemoryUnavailable;
        }
    }

    private async Task RefreshEnabledModCountAsync(GameInstance? instance)
    {
        if (instance is null || instance.Loader is LoaderKind.Vanilla)
        {
            enabledModCount = 0;
            RefreshSystemMemorySnapshot();
            return;
        }

        try
        {
            var mods = await modService.GetModsAsync(instance);
            if (SelectedInstance?.Instance.Id != instance.Id)
                return;

            enabledModCount = mods.Count(mod => mod.IsEnabled);
            RefreshSystemMemorySnapshot();
        }
        catch (Exception)
        {
            if (SelectedInstance?.Instance.Id == instance.Id)
            {
                enabledModCount = 0;
                RefreshSystemMemorySnapshot();
            }
        }
    }

    private SettingsMemoryModeOption ResolveMemoryModeOption(MemorySettingsMode mode)
    {
        return MemoryModeOptions.FirstOrDefault(option => option.Mode == mode)
               ?? MemoryModeOptions[0];
    }

    private GameSettingsLaunchSettingsModeOption ResolveLaunchSettingsModeOption(LaunchSettingsMode mode)
    {
        return LaunchSettingsModeOptions.FirstOrDefault(option => option.Mode == mode)
               ?? LaunchSettingsModeOptions[0];
    }

    private int NormalizeMemoryValue(double value)
    {
        return MemoryAllocationCalculator.NormalizeRecordedMemoryMb(value, MemorySliderMaximumMb);
    }

    private static string NormalizeSettingText(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }

    private void CancelPendingDescriptionSave()
    {
        descriptionSaveCancellationTokenSource?.Cancel();
        descriptionSaveCancellationTokenSource?.Dispose();
        descriptionSaveCancellationTokenSource = null;
    }

    private void ModManagement_DeleteModsRequested(ModDeleteRequest request)
    {
        DeleteModsRequested?.Invoke(request);
    }

    private void SaveManagement_DeleteSavesRequested(SaveDeleteRequest request)
    {
        DeleteSavesRequested?.Invoke(request);
    }

    private void SaveManagement_SaveImportFailedRequested(SaveImportFailureRequest request)
    {
        SaveImportFailedRequested?.Invoke(request);
    }

    private void ResourcePackManagement_DeleteResourcePacksRequested(ResourcePackDeleteRequest request)
    {
        DeleteResourcePacksRequested?.Invoke(request);
    }

    private void ResourcePackManagement_ResourcePackImportFailedRequested(ResourcePackImportFailureRequest request)
    {
        ResourcePackImportFailedRequested?.Invoke(request);
    }

    private void ShaderPackManagement_DeleteShaderPacksRequested(ShaderPackDeleteRequest request)
    {
        DeleteShaderPacksRequested?.Invoke(request);
    }

    private void ShaderPackManagement_ShaderPackImportFailedRequested(ShaderPackImportFailureRequest request)
    {
        ShaderPackImportFailedRequested?.Invoke(request);
    }

    public Task ReplaceImportedModAsync(string sourcePath)
    {
        return ModManagement.ReplaceImportedModAsync(sourcePath);
    }

    private void ModManagement_ImportModConflictRequested(ModImportConflictRequest request)
    {
        ImportModConflictRequested?.Invoke(request);
    }
}
