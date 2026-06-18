using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Settings;

public sealed partial class JavaSettingsEditorViewModel : ObservableObject
{
    private const string JavaSelectionAutoId = "auto";
    private const string JavaSelectionManualId = "manual";

    private readonly IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService;
    private readonly IStatusService statusService;
    private readonly IFilePickerService filePickerService;
    private readonly IFloatingMessageService floatingMessageService;
    private readonly Func<string?> minecraftDirectoryProvider;
    private CancellationTokenSource? javaRuntimeScanCancellationTokenSource;
    private string? savedSelectedJavaExecutablePath;
    private bool suppressSelectionChanged;

    [ObservableProperty]
    private SettingsJavaSelectionOption? selectedJavaSelectionOption;

    [ObservableProperty]
    private SettingsJavaRuntimeItem? selectedJavaRuntime;

    [ObservableProperty]
    private bool isJavaRuntimeScanRunning;

    [ObservableProperty]
    private string javaRuntimeListMessage = Strings.Settings_JavaListEmpty;

    [ObservableProperty]
    private bool isEditorEnabled = true;

    public JavaSettingsEditorViewModel(
        IJavaRuntimeDiscoveryService javaRuntimeDiscoveryService,
        IStatusService statusService,
        IFilePickerService filePickerService,
        IFloatingMessageService floatingMessageService,
        Func<string?> minecraftDirectoryProvider)
    {
        this.javaRuntimeDiscoveryService = javaRuntimeDiscoveryService;
        this.statusService = statusService;
        this.filePickerService = filePickerService;
        this.floatingMessageService = floatingMessageService;
        this.minecraftDirectoryProvider = minecraftDirectoryProvider;

        JavaSelectionOptions.Add(new SettingsJavaSelectionOption(JavaSelectionAutoId, Strings.Settings_JavaSelectionAuto));
        JavaSelectionOptions.Add(new SettingsJavaSelectionOption(JavaSelectionManualId, Strings.Settings_JavaSelectionManual));
        SelectedJavaSelectionOption = JavaSelectionOptions[0];
    }

    public ObservableCollection<SettingsJavaSelectionOption> JavaSelectionOptions { get; } = [];

    public ObservableCollection<SettingsJavaRuntimeItem> JavaRuntimes { get; } = [];

    public bool HasJavaRuntimeListMessage => !string.IsNullOrWhiteSpace(JavaRuntimeListMessage);

    public bool IsJavaManualSelection => SelectedMode is JavaSelectionMode.Manual;

    public JavaSelectionMode SelectedMode => SelectedJavaSelectionOption?.Id == JavaSelectionManualId
        ? JavaSelectionMode.Manual
        : JavaSelectionMode.Auto;

    public string? SelectedExecutablePath => SelectedMode is JavaSelectionMode.Manual && SelectedJavaRuntime is not null
        ? SelectedJavaRuntime.ExecutablePath
        : savedSelectedJavaExecutablePath;

    public event EventHandler? JavaSelectionChanged;

    public void LoadSelection(JavaSelectionMode mode, string? selectedJavaExecutablePath)
    {
        suppressSelectionChanged = true;
        try
        {
            savedSelectedJavaExecutablePath = string.IsNullOrWhiteSpace(selectedJavaExecutablePath)
                ? null
                : selectedJavaExecutablePath;
            SelectedJavaSelectionOption = GetJavaSelectionOption(mode);
            SelectedJavaRuntime = null;
            UpdateJavaRuntimeSelectionAfterListChanged();
        }
        finally
        {
            suppressSelectionChanged = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshJavaRuntimes))]
    public async Task RefreshJavaRuntimesAsync()
    {
        await RefreshJavaRuntimesCoreAsync(allowWhenDisabled: false);
    }

    public async Task RefreshJavaRuntimesForDisplayAsync()
    {
        await RefreshJavaRuntimesCoreAsync(allowWhenDisabled: true);
    }

    private async Task RefreshJavaRuntimesCoreAsync(bool allowWhenDisabled)
    {
        if (IsJavaRuntimeScanRunning || !allowWhenDisabled && !IsEditorEnabled)
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
                minecraftDirectoryProvider(),
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

    [RelayCommand(CanExecute = nameof(CanImportJavaRuntime))]
    public async Task ImportJavaRuntimeAsync()
    {
        if (!IsEditorEnabled)
            return;

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

    partial void OnSelectedJavaSelectionOptionChanged(SettingsJavaSelectionOption? value)
    {
        OnPropertyChanged(nameof(IsJavaManualSelection));
        OnPropertyChanged(nameof(SelectedMode));

        if (IsJavaManualSelection)
        {
            UpdateJavaRuntimeSelectionAfterListChanged();
        }
        else
        {
            suppressSelectionChanged = true;
            try
            {
                SelectedJavaRuntime = null;
            }
            finally
            {
                suppressSelectionChanged = false;
            }
        }

        RaiseJavaSelectionChanged();
    }

    partial void OnSelectedJavaRuntimeChanged(SettingsJavaRuntimeItem? value)
    {
        if (suppressSelectionChanged)
            return;

        if (!IsJavaManualSelection)
        {
            if (value is not null)
            {
                suppressSelectionChanged = true;
                try
                {
                    SelectedJavaRuntime = null;
                }
                finally
                {
                    suppressSelectionChanged = false;
                }
            }

            return;
        }

        if (value is not null)
            savedSelectedJavaExecutablePath = value.ExecutablePath;

        OnPropertyChanged(nameof(SelectedExecutablePath));
        RaiseJavaSelectionChanged();
    }

    partial void OnIsJavaRuntimeScanRunningChanged(bool value)
    {
        RefreshJavaRuntimesCommand.NotifyCanExecuteChanged();
    }

    partial void OnJavaRuntimeListMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasJavaRuntimeListMessage));
    }

    partial void OnIsEditorEnabledChanged(bool value)
    {
        RefreshJavaRuntimesCommand.NotifyCanExecuteChanged();
        ImportJavaRuntimeCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefreshJavaRuntimes()
    {
        return IsEditorEnabled && !IsJavaRuntimeScanRunning;
    }

    private bool CanImportJavaRuntime()
    {
        return IsEditorEnabled;
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

    private async Task EnsureSavedSelectedJavaRuntimePresentAsync(CancellationToken cancellationToken)
    {
        if (!IsJavaManualSelection || string.IsNullOrWhiteSpace(savedSelectedJavaExecutablePath))
            return;

        if (JavaRuntimes.Any(item => IsSameExecutablePath(item.ExecutablePath, savedSelectedJavaExecutablePath)))
            return;

        try
        {
            var runtime = await javaRuntimeDiscoveryService.DiscoverExecutableAsync(
                savedSelectedJavaExecutablePath,
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

        var savedRuntime = string.IsNullOrWhiteSpace(savedSelectedJavaExecutablePath)
            ? null
            : JavaRuntimes.FirstOrDefault(item => IsSameExecutablePath(item.ExecutablePath, savedSelectedJavaExecutablePath));
        var currentRuntime = SelectedJavaRuntime is null
            ? null
            : JavaRuntimes.FirstOrDefault(item => IsSameExecutablePath(item.ExecutablePath, SelectedJavaRuntime.ExecutablePath));

        SelectedJavaRuntime = savedRuntime ?? currentRuntime ?? JavaRuntimes.FirstOrDefault();
    }

    private SettingsJavaSelectionOption GetJavaSelectionOption(JavaSelectionMode mode)
    {
        var targetId = mode is JavaSelectionMode.Manual ? JavaSelectionManualId : JavaSelectionAutoId;
        return JavaSelectionOptions.FirstOrDefault(option => option.Id == targetId) ?? JavaSelectionOptions[0];
    }

    private void RaiseJavaSelectionChanged()
    {
        if (suppressSelectionChanged)
            return;

        OnPropertyChanged(nameof(SelectedExecutablePath));
        JavaSelectionChanged?.Invoke(this, EventArgs.Empty);
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
}
