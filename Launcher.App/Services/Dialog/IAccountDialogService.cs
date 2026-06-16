using System.Windows;
using Launcher.App.Controls;
using Launcher.Application.Accounts;

namespace Launcher.App.Services;

public interface IAccountDialogService
{
    void Attach(
        AccountPageViewModel accountPage,
        DialogHost addAccountHost,
        DialogHost deleteAccountHost,
        DialogHost renameAccountHost,
        DialogHost skinModelDialogHost);

    void ShowAddAccountDialog();

    void ShowDeleteAccountDialog(LauncherAccount account);

    void ShowRenameAccountDialog();

    void ShowSkinModelDialog(string skinFilePath);

    void ShowSkinFormatErrorDialog();

    void CancelAddAccountDialog();

    void BackAddAccountDialog();

    Task ConfirmAddAccountDialogAsync();

    void CancelDeleteAccountDialog();

    Task ConfirmDeleteAccountDialogAsync();

    void CancelRenameAccountDialog();

    Task ConfirmRenameAccountDialogAsync();

    void CancelSkinModelDialog();

    Task ConfirmSkinModelDialogAsync();

    void QueueOpenDialogBlurRefresh();

    void Prewarm();
}

