using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Services;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class GameSettingsEditDialogViewModel : ObservableObject
{
    private const string InputGlyph = "\uE70F";
    private const string BusyGlyph = "\uE895";

    private readonly IGameInstanceService instanceService;
    private readonly IStatusService statusService;
    private string originalResolvedIconSource = string.Empty;
    private string? originalExplicitIconSource;

    [ObservableProperty]
    private bool isEditInstanceDialogOpen;

    [ObservableProperty]
    private bool isEditInstanceDialogBusy;

    [ObservableProperty]
    private GameSettingsInstanceItem? instancePendingEdit;

    [ObservableProperty]
    private string editInstanceDialogStep = GameSettingsDialogSteps.Input;

    [ObservableProperty]
    private string instanceName = string.Empty;

    [ObservableProperty]
    private bool isInstanceNameInvalid;

    [ObservableProperty]
    private GameSettingsIconOption? selectedIconOption;

    [ObservableProperty]
    private bool isEditInstanceSuccessful;

    [ObservableProperty]
    private string editInstanceMessage = string.Empty;

    [ObservableProperty]
    private string editInstanceGlyph = InputGlyph;

    public GameSettingsEditDialogViewModel(
        IGameInstanceService instanceService,
        IStatusService statusService)
    {
        this.instanceService = instanceService;
        this.statusService = statusService;

        foreach (var option in GameSettingsIconOptionFactory.Create())
            IconOptions.Add(option);
    }

    public event Action<GameInstance>? InstanceUpdated;

    public ObservableCollection<GameSettingsIconOption> IconOptions { get; } = [];

    public bool IsEditInstanceInputStep => EditInstanceDialogStep == GameSettingsDialogSteps.Input;

    public bool IsEditInstanceStatusStep => EditInstanceDialogStep == GameSettingsDialogSteps.Status;

    public bool IsEditInstanceResultStep => EditInstanceDialogStep == GameSettingsDialogSteps.Result;

    public bool IsEditInstanceMessageStep => IsEditInstanceStatusStep || IsEditInstanceResultStep;

    public bool CanShowEditInstanceCancelButton => !IsEditInstanceDialogBusy && IsEditInstanceInputStep;

    public bool CanConfirmEditInstanceDialog => !IsEditInstanceDialogBusy
        && (IsEditInstanceResultStep
            || (IsEditInstanceInputStep
                && !string.IsNullOrWhiteSpace(InstanceName)
                && SelectedIconOption is not null));

    public string? EditInstanceGlyphIconKey => IsEditInstanceResultStep
        ? IsEditInstanceSuccessful ? "general/general_passed" : "general/general_attention"
        : null;

    public string EditInstanceDialogTitle => EditInstanceDialogStep switch
    {
        GameSettingsDialogSteps.Status => Strings.Dialog_RenameInstanceBusyTitle,
        GameSettingsDialogSteps.Result => IsEditInstanceSuccessful
            ? Strings.Dialog_RenameInstanceSuccessTitle
            : Strings.Dialog_RenameInstanceFailedTitle,
        _ => Strings.Dialog_RenameInstanceTitle
    };

    public string EditInstanceDialogSubtitle => EditInstanceDialogStep switch
    {
        GameSettingsDialogSteps.Status => Strings.Dialog_RenameInstanceBusySubtitle,
        GameSettingsDialogSteps.Result => Strings.Dialog_RenameInstanceResultSubtitle,
        _ => Strings.Dialog_RenameInstanceSubtitle
    };

    public void Open(GameSettingsInstanceItem instance)
    {
        InstancePendingEdit = instance;
        originalResolvedIconSource = instance.IconSource;
        originalExplicitIconSource = string.IsNullOrWhiteSpace(instance.Instance.IconSource)
            ? null
            : instance.Instance.IconSource;
        ResetEditInstanceDialogState(instance.Name, ResolveInitialIconOption(instance));
        IsEditInstanceDialogOpen = true;
    }

    public void Cancel()
    {
        if (IsEditInstanceDialogBusy)
            return;

        IsEditInstanceDialogOpen = false;
    }

    public async Task ConfirmAsync()
    {
        if (IsEditInstanceDialogBusy)
            return;

        if (IsEditInstanceResultStep)
        {
            IsEditInstanceDialogOpen = false;
            return;
        }

        var pendingEdit = InstancePendingEdit;
        var selectedIcon = SelectedIconOption;
        if (pendingEdit is null || selectedIcon is null)
            return;

        var newName = InstanceName.Trim();
        if (!IsValidInstanceName(newName))
        {
            IsInstanceNameInvalid = true;
            return;
        }

        var resolvedIconSource = ResolveIconSourceToPersist(selectedIcon);
        var isNameUnchanged = string.Equals(newName, pendingEdit.Instance.Name, StringComparison.Ordinal)
            && string.Equals(newName, pendingEdit.Instance.VersionName, StringComparison.OrdinalIgnoreCase);
        var isIconUnchanged = string.Equals(resolvedIconSource, pendingEdit.Instance.IconSource, StringComparison.Ordinal);
        if (isNameUnchanged && isIconUnchanged)
        {
            statusService.Report(Strings.Status_InstanceRenameUnchanged);
            CloseAfterSuccess();
            return;
        }

        try
        {
            IsEditInstanceDialogBusy = true;
            EditInstanceDialogStep = GameSettingsDialogSteps.Status;
            EditInstanceGlyph = BusyGlyph;
            EditInstanceMessage = Strings.Status_RenamingInstance;

            var updatedInstance = await instanceService.RenameInstanceAsync(
                pendingEdit.Instance.Id,
                newName,
                resolvedIconSource);

            statusService.Report(string.Format(Strings.Status_InstanceRenamedFormat, updatedInstance.Name));
            InstanceUpdated?.Invoke(updatedInstance);
            CloseAfterSuccess();
        }
        catch (DuplicateGameInstanceNameException)
        {
            statusService.Report(Strings.Status_DuplicateInstanceName);
            ShowEditInstanceResult(false, Strings.Status_DuplicateInstanceName);
        }
        catch (Exception)
        {
            statusService.Report(Strings.Status_InstanceRenameFailed);
            ShowEditInstanceResult(false, Strings.Status_InstanceRenameFailed);
        }
        finally
        {
            IsEditInstanceDialogBusy = false;
        }
    }

    partial void OnEditInstanceDialogStepChanged(string value)
    {
        OnPropertyChanged(nameof(IsEditInstanceInputStep));
        OnPropertyChanged(nameof(IsEditInstanceStatusStep));
        OnPropertyChanged(nameof(IsEditInstanceResultStep));
        OnPropertyChanged(nameof(IsEditInstanceMessageStep));
        OnPropertyChanged(nameof(EditInstanceDialogTitle));
        OnPropertyChanged(nameof(EditInstanceDialogSubtitle));
        OnPropertyChanged(nameof(EditInstanceGlyphIconKey));
        OnPropertyChanged(nameof(CanShowEditInstanceCancelButton));
        OnPropertyChanged(nameof(CanConfirmEditInstanceDialog));
    }

    partial void OnIsEditInstanceDialogBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowEditInstanceCancelButton));
        OnPropertyChanged(nameof(CanConfirmEditInstanceDialog));
    }

    partial void OnInstanceNameChanged(string value)
    {
        IsInstanceNameInvalid = false;
        OnPropertyChanged(nameof(CanConfirmEditInstanceDialog));
    }

    partial void OnSelectedIconOptionChanged(GameSettingsIconOption? value)
    {
        OnPropertyChanged(nameof(CanConfirmEditInstanceDialog));
    }

    partial void OnIsEditInstanceSuccessfulChanged(bool value)
    {
        OnPropertyChanged(nameof(EditInstanceDialogTitle));
        OnPropertyChanged(nameof(EditInstanceGlyphIconKey));
    }

    private void ResetEditInstanceDialogState(string currentName, GameSettingsIconOption? iconOption)
    {
        IsEditInstanceDialogBusy = false;
        EditInstanceDialogStep = GameSettingsDialogSteps.Input;
        InstanceName = currentName;
        IsInstanceNameInvalid = false;
        SelectedIconOption = iconOption ?? IconOptions.FirstOrDefault();
        IsEditInstanceSuccessful = false;
        EditInstanceMessage = string.Empty;
        EditInstanceGlyph = InputGlyph;
    }

    private void ShowEditInstanceResult(bool isSuccess, string message)
    {
        IsEditInstanceSuccessful = isSuccess;
        EditInstanceMessage = message;
        EditInstanceDialogStep = GameSettingsDialogSteps.Result;
    }

    private void CloseAfterSuccess()
    {
        IsEditInstanceSuccessful = true;
        IsEditInstanceDialogOpen = false;
    }

    private string? ResolveIconSourceToPersist(GameSettingsIconOption selectedIcon)
    {
        return string.IsNullOrWhiteSpace(originalExplicitIconSource)
               && string.Equals(selectedIcon.IconSource, originalResolvedIconSource, StringComparison.OrdinalIgnoreCase)
            ? null
            : selectedIcon.IconSource;
    }

    private GameSettingsIconOption? ResolveInitialIconOption(GameSettingsInstanceItem instance)
    {
        var preferredIconSource = string.IsNullOrWhiteSpace(instance.Instance.IconSource)
            ? instance.IconSource
            : instance.Instance.IconSource;
        return IconOptions.FirstOrDefault(option =>
            string.Equals(option.IconSource, preferredIconSource, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidInstanceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var invalidCharacters = Path.GetInvalidFileNameChars();
        return value.All(character => !invalidCharacters.Contains(character));
    }
}
