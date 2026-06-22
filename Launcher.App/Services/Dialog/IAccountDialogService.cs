using System.Windows;
using Launcher.App.Controls;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.App.Services;

public interface IAccountDialogService
{
    void Attach(
        AccountPageViewModel accountPage,
        DialogHost addAccountHost,
        DialogHost deleteAccountHost,
        DialogHost renameAccountHost,
        DialogHost skinModelDialogHost,
        DialogHost skinManagerDialogHost);

    void ShowAddAccountDialog();

    void ShowDeleteAccountDialog(LauncherAccount account);

    void ShowRenameAccountDialog();

    void ShowSkinModelDialog(string skinFilePath);

    void ShowSkinModelDialog(MinecraftSkinModel skinModel);

    void ShowSkinFormatErrorDialog();

    void ShowSkinManagerDialog();

    void CancelAddAccountDialog();

    void BackAddAccountDialog();

    Task ConfirmAddAccountDialogAsync();

    void CancelDeleteAccountDialog();

    Task ConfirmDeleteAccountDialogAsync();

    void CancelRenameAccountDialog();

    Task ConfirmRenameAccountDialogAsync();

    void CancelSkinModelDialog();

    Task ConfirmSkinModelDialogAsync();

    void CancelSkinManagerDialog();

    void Prewarm();
}

