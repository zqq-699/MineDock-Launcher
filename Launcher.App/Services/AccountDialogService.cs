using System.Windows;
using Launcher.App.Controls;
using Launcher.App.ViewModels;
using Launcher.Application.Accounts;

namespace Launcher.App.Services;

public sealed class AccountDialogService : IAccountDialogService
{
    private const int BlurRefreshAttempts = 5;

    private AccountPageViewModel? accountPage;
    private DialogOverlayService? overlayService;
    private DialogHost? addAccountHost;
    private DialogHost? deleteAccountHost;
    private DialogHost? renameAccountHost;
    private DialogHost? skinModelDialogHost;

    public void Attach(
        AccountPageViewModel accountPage,
        Window owner,
        FrameworkElement contentLayer,
        DialogHost addAccountHost,
        DialogHost deleteAccountHost,
        DialogHost renameAccountHost,
        DialogHost skinModelDialogHost)
    {
        this.accountPage = accountPage;
        overlayService = new DialogOverlayService(owner, contentLayer);
        this.addAccountHost = addAccountHost;
        this.deleteAccountHost = deleteAccountHost;
        this.renameAccountHost = renameAccountHost;
        this.skinModelDialogHost = skinModelDialogHost;

        addAccountHost.SurfaceBorder.SizeChanged += (_, _) => QueueDialogBlurRefreshWhenIdle();
        deleteAccountHost.SurfaceBorder.SizeChanged += (_, _) => QueueDialogBlurRefreshWhenIdle();
        renameAccountHost.SurfaceBorder.SizeChanged += (_, _) => QueueDialogBlurRefreshWhenIdle();
        skinModelDialogHost.SurfaceBorder.SizeChanged += (_, _) => QueueDialogBlurRefreshWhenIdle();
    }

    public void ShowAddAccountDialog()
    {
        if (accountPage is null || overlayService is null || addAccountHost is null)
            return;

        accountPage.OpenAddAccountDialog();
        overlayService.Show(addAccountHost);
    }

    public void ShowDeleteAccountDialog(LauncherAccount account)
    {
        if (accountPage is null || overlayService is null || deleteAccountHost is null)
            return;

        accountPage.OpenDeleteAccountDialog(account);
        overlayService.Show(deleteAccountHost);
    }

    public void ShowRenameAccountDialog()
    {
        if (accountPage is null || overlayService is null || renameAccountHost is null)
            return;

        accountPage.OpenRenameAccountDialog();
        if (accountPage.IsRenameAccountDialogOpen)
            overlayService.Show(renameAccountHost);
    }

    public void ShowSkinModelDialog(string skinFilePath)
    {
        if (accountPage is null || overlayService is null || skinModelDialogHost is null)
            return;

        accountPage.OpenSkinModelDialog(skinFilePath);
        overlayService.Show(skinModelDialogHost);
    }

    public void ShowSkinFormatErrorDialog()
    {
        if (accountPage is null || overlayService is null || skinModelDialogHost is null)
            return;

        accountPage.OpenSkinFormatErrorDialog();
        overlayService.Show(skinModelDialogHost);
    }

    public void CancelAddAccountDialog()
    {
        if (accountPage is null || overlayService is null || addAccountHost is null)
            return;

        accountPage.CancelAddAccountDialog();
        if (!accountPage.IsAddAccountDialogOpen)
            overlayService.Hide(addAccountHost, accountPage.ResetAddAccountDialog);
    }

    public void BackAddAccountDialog()
    {
        if (accountPage is null || overlayService is null || addAccountHost is null)
            return;

        var previousHeight = addAccountHost.SurfaceBorder.ActualHeight;
        accountPage.BackToAddAccountTypeStep();
        overlayService.AnimateSizeChange(addAccountHost, previousHeight);
    }

    public async Task ConfirmAddAccountDialogAsync()
    {
        if (accountPage is null || overlayService is null || addAccountHost is null)
            return;

        var previousHeight = addAccountHost.SurfaceBorder.ActualHeight;

        if (accountPage.IsAccountTypeStep && accountPage.SelectedAccountTypeOption?.Kind is AccountTypeKinds.Microsoft)
        {
            accountPage.BeginMicrosoftAccountLogin();
            overlayService.AnimateSizeChange(addAccountHost, previousHeight);

            var loginHeight = addAccountHost.SurfaceBorder.ActualHeight;
            await accountPage.CompleteMicrosoftAccountLoginAsync();
            overlayService.AnimateSizeChange(addAccountHost, loginHeight);
            return;
        }

        await accountPage.ConfirmAddAccountDialogAsync();
        if (accountPage.IsAddAccountDialogOpen)
            overlayService.AnimateSizeChange(addAccountHost, previousHeight);
        else
            overlayService.Hide(addAccountHost, accountPage.ResetAddAccountDialog);
    }

    public void CancelDeleteAccountDialog()
    {
        if (accountPage is null || overlayService is null || deleteAccountHost is null)
            return;

        accountPage.CancelDeleteAccountDialog();
        overlayService.Hide(deleteAccountHost);
    }

    public async Task ConfirmDeleteAccountDialogAsync()
    {
        if (accountPage is null || overlayService is null || deleteAccountHost is null)
            return;

        var deleteTask = accountPage.ConfirmDeleteAccountDialogAsync();
        if (!accountPage.IsDeleteAccountDialogOpen)
        {
            overlayService.Hide(deleteAccountHost);
            await deleteTask;
            return;
        }

        await deleteTask;
        if (!accountPage.IsDeleteAccountDialogOpen)
            overlayService.Hide(deleteAccountHost);
    }

    public void CancelRenameAccountDialog()
    {
        if (accountPage is null || overlayService is null || renameAccountHost is null)
            return;

        accountPage.CancelRenameAccountDialog();
        if (!accountPage.IsRenameAccountDialogOpen)
            overlayService.Hide(renameAccountHost, accountPage.ResetRenameAccountDialog);
    }

    public async Task ConfirmRenameAccountDialogAsync()
    {
        if (accountPage is null || overlayService is null || renameAccountHost is null)
            return;

        var previousHeight = renameAccountHost.SurfaceBorder.ActualHeight;
        await accountPage.ConfirmRenameAccountDialogAsync();

        if (accountPage.IsRenameAccountDialogOpen)
            overlayService.AnimateSizeChange(renameAccountHost, previousHeight);
        else
            overlayService.Hide(renameAccountHost, accountPage.ResetRenameAccountDialog);
    }

    public void CancelSkinModelDialog()
    {
        if (accountPage is null || overlayService is null || skinModelDialogHost is null)
            return;

        accountPage.CancelSkinModelDialog();
        overlayService.Hide(skinModelDialogHost, accountPage.ResetSkinModelDialog);
    }

    public async Task ConfirmSkinModelDialogAsync()
    {
        if (accountPage is null || overlayService is null || skinModelDialogHost is null)
            return;

        var confirmTask = accountPage.ConfirmSkinModelDialogAsync();
        if (!accountPage.IsSkinModelDialogOpen)
        {
            overlayService.Hide(skinModelDialogHost, accountPage.ResetSkinModelDialog);
            await confirmTask;
            return;
        }

        await confirmTask;
        if (!accountPage.IsSkinModelDialogOpen)
            overlayService.Hide(skinModelDialogHost, accountPage.ResetSkinModelDialog);
    }

    public void QueueOpenDialogBlurRefresh()
    {
        if (accountPage is null || overlayService is null)
            return;

        if (accountPage.IsAddAccountDialogOpen && addAccountHost is not null)
            overlayService.QueueRefresh(addAccountHost, BlurRefreshAttempts);

        if (accountPage.IsDeleteAccountDialogOpen && deleteAccountHost is not null)
            overlayService.QueueRefresh(deleteAccountHost, BlurRefreshAttempts);

        if (accountPage.IsRenameAccountDialogOpen && renameAccountHost is not null)
            overlayService.QueueRefresh(renameAccountHost, BlurRefreshAttempts);

        if (accountPage.IsSkinModelDialogOpen && skinModelDialogHost is not null)
            overlayService.QueueRefresh(skinModelDialogHost, BlurRefreshAttempts);
    }

    public void Prewarm()
    {
        if (overlayService is null)
            return;

        if (addAccountHost is not null)
            overlayService.Prewarm(addAccountHost);
        if (deleteAccountHost is not null)
            overlayService.Prewarm(deleteAccountHost);
        if (renameAccountHost is not null)
            overlayService.Prewarm(renameAccountHost);
        if (skinModelDialogHost is not null)
            overlayService.Prewarm(skinModelDialogHost);
    }

    private void QueueDialogBlurRefreshWhenIdle()
    {
        if (overlayService is { IsSizeAnimating: false })
            QueueOpenDialogBlurRefresh();
    }
}
