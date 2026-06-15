using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;

namespace Launcher.App.ViewModels;

public sealed partial class AccountSkinModelDialogViewModel : ObservableObject
{
    private string pendingSkinFilePath = string.Empty;

    [ObservableProperty]
    private bool isSkinModelDialogOpen;

    [ObservableProperty]
    private bool isSkinFormatError;

    [ObservableProperty]
    private AccountSkinModelOption? selectedSkinModelOption;

    public ObservableCollection<AccountSkinModelOption> SkinModelOptions { get; } = new(AccountSkinModelOptionFactory.Create());

    public bool CanConfirmSkinModelDialog => IsSkinModelDialogOpen
        && (IsSkinFormatError
            || (!string.IsNullOrWhiteSpace(pendingSkinFilePath) && SelectedSkinModelOption is not null));

    public bool IsSkinModelSelectionStep => IsSkinModelDialogOpen && !IsSkinFormatError;

    public bool CanShowSkinModelDialogCancelButton => IsSkinModelSelectionStep;

    public string SkinModelDialogTitle => IsSkinFormatError
        ? Strings.Dialog_SkinFormatErrorTitle
        : Strings.Dialog_SkinModelTitle;

    public string SkinModelDialogSubtitle => IsSkinFormatError
        ? Strings.Dialog_SkinFormatErrorSubtitle
        : Strings.Dialog_SkinModelSubtitle;

    public void Open(string skinFilePath)
    {
        pendingSkinFilePath = skinFilePath;
        IsSkinFormatError = false;
        SelectedSkinModelOption = null;
        IsSkinModelDialogOpen = true;
        NotifyDialogStateChanged();
    }

    public void OpenFormatError()
    {
        pendingSkinFilePath = string.Empty;
        SelectedSkinModelOption = null;
        IsSkinFormatError = true;
        IsSkinModelDialogOpen = true;
        NotifyDialogStateChanged();
    }

    public void Cancel()
    {
        IsSkinModelDialogOpen = false;
        Reset();
    }

    public void Reset()
    {
        pendingSkinFilePath = string.Empty;
        IsSkinFormatError = false;
        SelectedSkinModelOption = null;
        NotifyDialogStateChanged();
    }

    public bool TryConsumeSelection(out string skinFilePath, out MinecraftSkinModel skinModel)
    {
        skinFilePath = string.Empty;
        skinModel = MinecraftSkinModel.Classic;

        if (!CanConfirmSkinModelDialog || SelectedSkinModelOption is null)
            return false;

        skinFilePath = pendingSkinFilePath;
        skinModel = SelectedSkinModelOption.Model;
        IsSkinModelDialogOpen = false;
        Reset();
        return true;
    }

    partial void OnIsSkinModelDialogOpenChanged(bool value)
    {
        NotifyDialogStateChanged();
    }

    partial void OnIsSkinFormatErrorChanged(bool value)
    {
        NotifyDialogStateChanged();
    }

    partial void OnSelectedSkinModelOptionChanged(AccountSkinModelOption? value)
    {
        OnPropertyChanged(nameof(CanConfirmSkinModelDialog));
    }

    private void NotifyDialogStateChanged()
    {
        OnPropertyChanged(nameof(CanConfirmSkinModelDialog));
        OnPropertyChanged(nameof(IsSkinModelSelectionStep));
        OnPropertyChanged(nameof(CanShowSkinModelDialogCancelButton));
        OnPropertyChanged(nameof(SkinModelDialogTitle));
        OnPropertyChanged(nameof(SkinModelDialogSubtitle));
    }
}
