using Launcher.App.Models;
using Launcher.App.ViewModels;

namespace Launcher.App.Controls;

public sealed class AccountDialogController
{
    private const int BlurRefreshAttempts = 5;

    private readonly Func<AccountPageViewModel?> accountPageAccessor;
    private readonly DialogOverlayController overlayController;
    private readonly DialogHost addAccountHost;
    private readonly DialogHost deleteAccountHost;
    private readonly DialogHost renameAccountHost;

    public AccountDialogController(
        Func<AccountPageViewModel?> accountPageAccessor,
        DialogOverlayController overlayController,
        DialogHost addAccountHost,
        DialogHost deleteAccountHost,
        DialogHost renameAccountHost)
    {
        this.accountPageAccessor = accountPageAccessor;
        this.overlayController = overlayController;
        this.addAccountHost = addAccountHost;
        this.deleteAccountHost = deleteAccountHost;
        this.renameAccountHost = renameAccountHost;
    }

    private AccountPageViewModel? AccountPage => accountPageAccessor();

    public void AttachSizeInvalidation(Action invalidate)
    {
        addAccountHost.SurfaceBorder.SizeChanged += (_, _) => invalidate();
        deleteAccountHost.SurfaceBorder.SizeChanged += (_, _) => invalidate();
        renameAccountHost.SurfaceBorder.SizeChanged += (_, _) => invalidate();
    }

    public void ShowAddAccountDialog()
    {
        if (AccountPage is not { } accountPage)
            return;

        accountPage.OpenAddAccountDialog();
        overlayController.Show(addAccountHost);
    }

    public void ShowDeleteAccountDialog(LauncherAccount account)
    {
        if (AccountPage is not { } accountPage)
            return;

        accountPage.OpenDeleteAccountDialog(account);
        overlayController.Show(deleteAccountHost);
    }

    public void ShowRenameAccountDialog()
    {
        if (AccountPage is not { } accountPage)
            return;

        accountPage.OpenRenameAccountDialog();
        if (accountPage.IsRenameAccountDialogOpen)
            overlayController.Show(renameAccountHost);
    }

    public void CancelAddAccountDialog()
    {
        if (AccountPage is not { } accountPage)
            return;

        accountPage.CancelAddAccountDialog();
        if (!accountPage.IsAddAccountDialogOpen)
            overlayController.Hide(addAccountHost, accountPage.ResetAddAccountDialog);
    }

    public void BackAddAccountDialog()
    {
        if (AccountPage is not { } accountPage)
            return;

        var previousHeight = addAccountHost.SurfaceBorder.ActualHeight;
        accountPage.BackToAddAccountTypeStep();
        overlayController.AnimateSizeChange(addAccountHost, previousHeight);
    }

    public async Task ConfirmAddAccountDialogAsync()
    {
        if (AccountPage is not { } accountPage)
            return;

        var previousHeight = addAccountHost.SurfaceBorder.ActualHeight;

        if (accountPage.IsAccountTypeStep && accountPage.SelectedAccountTypeOption?.Kind is AccountTypeKinds.Microsoft)
        {
            accountPage.BeginMicrosoftAccountLogin();
            overlayController.AnimateSizeChange(addAccountHost, previousHeight);

            var loginHeight = addAccountHost.SurfaceBorder.ActualHeight;
            await accountPage.CompleteMicrosoftAccountLoginAsync();
            overlayController.AnimateSizeChange(addAccountHost, loginHeight);
            return;
        }

        await accountPage.ConfirmAddAccountDialogAsync();
        if (accountPage.IsAddAccountDialogOpen)
            overlayController.AnimateSizeChange(addAccountHost, previousHeight);
        else
            overlayController.Hide(addAccountHost, accountPage.ResetAddAccountDialog);
    }

    public void CancelDeleteAccountDialog()
    {
        if (AccountPage is not { } accountPage)
            return;

        accountPage.CancelDeleteAccountDialog();
        overlayController.Hide(deleteAccountHost);
    }

    public async Task ConfirmDeleteAccountDialogAsync()
    {
        if (AccountPage is not { } accountPage)
            return;

        var deleteTask = accountPage.ConfirmDeleteAccountDialogAsync();
        if (!accountPage.IsDeleteAccountDialogOpen)
            overlayController.Hide(deleteAccountHost);

        await deleteTask;
    }

    public void CancelRenameAccountDialog()
    {
        if (AccountPage is not { } accountPage)
            return;

        accountPage.CancelRenameAccountDialog();
        if (!accountPage.IsRenameAccountDialogOpen)
            overlayController.Hide(renameAccountHost, accountPage.ResetRenameAccountDialog);
    }

    public async Task ConfirmRenameAccountDialogAsync()
    {
        if (AccountPage is not { } accountPage)
            return;

        var previousHeight = renameAccountHost.SurfaceBorder.ActualHeight;
        await accountPage.ConfirmRenameAccountDialogAsync();

        if (accountPage.IsRenameAccountDialogOpen)
            overlayController.AnimateSizeChange(renameAccountHost, previousHeight);
        else
            overlayController.Hide(renameAccountHost, accountPage.ResetRenameAccountDialog);
    }

    public void QueueOpenDialogBlurRefresh()
    {
        if (AccountPage is not { } accountPage)
            return;

        if (accountPage.IsAddAccountDialogOpen)
            overlayController.QueueRefresh(addAccountHost, BlurRefreshAttempts);

        if (accountPage.IsDeleteAccountDialogOpen)
            overlayController.QueueRefresh(deleteAccountHost, BlurRefreshAttempts);

        if (accountPage.IsRenameAccountDialogOpen)
            overlayController.QueueRefresh(renameAccountHost, BlurRefreshAttempts);
    }

    public void Prewarm()
    {
        overlayController.Prewarm(addAccountHost);
        overlayController.Prewarm(deleteAccountHost);
        overlayController.Prewarm(renameAccountHost);
    }
}
