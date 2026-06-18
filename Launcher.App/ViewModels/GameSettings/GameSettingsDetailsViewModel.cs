using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsDetailsViewModel : ObservableObject
{
    private readonly GameSettingsEditDialogViewModel editDialog;
    private readonly IGameInstanceService instanceService;
    private readonly IStatusService statusService;
    private readonly IInstanceFolderService instanceFolderService;
    private INotifyPropertyChanged? selectedInstanceNotifier;
    private LauncherSettings globalSettings = new();
    private CancellationTokenSource? descriptionSaveCancellationTokenSource;
    private bool suppressInstanceSettingsAutoSave;

    [ObservableProperty]
    private GameSettingsInstanceItem? selectedInstance;

    [ObservableProperty]
    private GameSettingsDetailSectionItem? selectedSection;

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
    private GameSettingsLaunchSettingsModeOption? selectedLaunchSettingsModeOption;

    public GameSettingsDetailsViewModel(
        GameSettingsEditDialogViewModel editDialog,
        IGameInstanceService instanceService,
        IStatusService statusService,
        IInstanceFolderService instanceFolderService)
    {
        this.editDialog = editDialog;
        this.instanceService = instanceService;
        this.statusService = statusService;
        this.instanceFolderService = instanceFolderService;
    }

    public bool HasSelectedInstance => SelectedInstance is not null;

    public string InstanceName => SelectedInstance?.Name ?? string.Empty;

    public string InstanceIconSource => SelectedInstance?.IconSource ?? string.Empty;

    public string InstanceSubtitle => SelectedInstance?.Subtitle ?? string.Empty;

    public string SectionTitle => SelectedSection?.Title ?? Strings.GameSettings_DetailGeneral;

    public string SectionPlaceholderBody => string.Format(
        Strings.GameSettings_DetailPlaceholderBodyFormat,
        SectionTitle);

    public bool IsGeneralSection => string.Equals(SelectedSection?.Id, "general", StringComparison.OrdinalIgnoreCase);

    public bool IsLaunchSection => string.Equals(SelectedSection?.Id, "launch", StringComparison.OrdinalIgnoreCase);

    public bool IsJavaMemorySection => string.Equals(SelectedSection?.Id, "java_memory", StringComparison.OrdinalIgnoreCase);

    public bool AreLaunchSettingsOverridesEnabled => SelectedLaunchSettingsModeOption?.Mode is LaunchSettingsMode.PerInstance;

    public bool CanEditAutoRepairMissingFiles => AreLaunchSettingsOverridesEnabled && LaunchCheckFilesBeforeLaunchEnabled;

    public string InstanceCreatedAtText => SelectedInstance?.Instance.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty;

    public IReadOnlyList<GameSettingsLaunchSettingsModeOption> LaunchSettingsModeOptions { get; } =
    [
        new(LaunchSettingsMode.UseGlobal, Strings.GameSettings_LaunchSettingsModeUseGlobal),
        new(LaunchSettingsMode.PerInstance, Strings.GameSettings_LaunchSettingsModePerInstance)
    ];

    public void PrimeFromSettings(LauncherSettings launcherSettings)
    {
        globalSettings = launcherSettings;

        if (SelectedLaunchSettingsModeOption?.Mode is LaunchSettingsMode.UseGlobal)
            ApplyGlobalLaunchSettingsToEditor();
    }

    public void SetSelectedInstance(GameSettingsInstanceItem? instance)
    {
        SelectedInstance = instance;
    }

    public void SetSelectedSection(GameSettingsDetailSectionItem? section)
    {
        SelectedSection = section;
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
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            statusService.Report(Strings.Status_InstanceFolderNotFound);
            return;
        }

        if (!instanceFolderService.TryOpen(folderPath))
            statusService.Report(Strings.Status_OpenInstanceFolderFailed);
    }

    partial void OnSelectedInstanceChanged(GameSettingsInstanceItem? value)
    {
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged -= SelectedInstance_PropertyChanged;

        selectedInstanceNotifier = value;
        if (selectedInstanceNotifier is not null)
            selectedInstanceNotifier.PropertyChanged += SelectedInstance_PropertyChanged;

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

            if (mode is LaunchSettingsMode.UseGlobal)
            {
                ApplyGlobalLaunchSettingsToEditor();
            }
            else
            {
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
    }

    partial void OnSelectedSectionChanged(GameSettingsDetailSectionItem? value)
    {
        OnPropertyChanged(nameof(IsGeneralSection));
        OnPropertyChanged(nameof(IsLaunchSection));
        OnPropertyChanged(nameof(IsJavaMemorySection));
        OnPropertyChanged(nameof(SectionTitle));
        OnPropertyChanged(nameof(SectionPlaceholderBody));
    }

    partial void OnSelectedLaunchSettingsModeOptionChanged(GameSettingsLaunchSettingsModeOption? value)
    {
        OnPropertyChanged(nameof(AreLaunchSettingsOverridesEnabled));
        OnPropertyChanged(nameof(CanEditAutoRepairMissingFiles));

        if (suppressInstanceSettingsAutoSave)
            return;

        if (value?.Mode is LaunchSettingsMode.UseGlobal)
            ApplyGlobalLaunchSettingsToEditor();

        SaveLaunchSettings();
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

    private void SelectedInstance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(InstanceName));
        OnPropertyChanged(nameof(InstanceIconSource));
        OnPropertyChanged(nameof(InstanceSubtitle));
        OnPropertyChanged(nameof(InstanceCreatedAtText));
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
            gameArguments);
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
        string gameArguments)
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

        if (originalMode == mode
            && originalCheckFilesBeforeLaunch == checkFilesBeforeLaunch
            && originalAutoRepairMissingFiles == autoRepairMissingFiles
            && originalMinimizeLauncherAfterLaunch == minimizeLauncherAfterLaunch
            && originalLaunchFullScreen == launchFullScreen
            && string.Equals(originalPreLaunchCommand, preLaunchCommand, StringComparison.Ordinal)
            && originalWaitForPreLaunchCommand == waitForPreLaunchCommand
            && string.Equals(originalPostExitCommand, postExitCommand, StringComparison.Ordinal)
            && string.Equals(originalJvmArguments, jvmArguments, StringComparison.Ordinal)
            && string.Equals(originalGameArguments, gameArguments, StringComparison.Ordinal))
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
            await instanceService.SaveInstanceAsync(instance);
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

            suppressInstanceSettingsAutoSave = true;
            SelectedLaunchSettingsModeOption = ResolveLaunchSettingsModeOption(originalMode);
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

    private GameSettingsLaunchSettingsModeOption ResolveLaunchSettingsModeOption(LaunchSettingsMode mode)
    {
        return LaunchSettingsModeOptions.FirstOrDefault(option => option.Mode == mode)
               ?? LaunchSettingsModeOptions[0];
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
}
