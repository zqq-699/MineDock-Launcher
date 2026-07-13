/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountDialogViewModel
{
    public void OpenAddAccountDialog()
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        IsAddAccountDialogOpen = true;
    }

    public void OpenThirdPartyReauthenticationDialog(LauncherAccount account)
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        AccountPendingThirdPartyReauthentication = account;
        AddAccountDialogStep = AccountDialogSteps.AddAccountThirdPartyReauthentication;
        ThirdParty.PrepareReauthentication(account);
        IsAddAccountDialogOpen = true;
    }

    public void OpenMicrosoftReauthenticationDialog(LauncherAccount account)
    {
        ResetAddAccountDialogState(clearOfflineName: true);
        AccountPendingMicrosoftReauthentication = account;
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftReauthentication;
        IsAddAccountDialogBusy = true;
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        IsAddAccountDialogOpen = true;
        ReportStatus(Strings.Status_OpeningMicrosoftLogin);
    }

    public void CancelAddAccountDialog()
    {
        if (IsThirdPartyImportProgressStep)
        {
            thirdPartyImportCancellationTokenSource?.Cancel();
            return;
        }
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
        _ = ThirdParty.CancelEmailLoginAsync();
    }

    public void ResetAddAccountDialog()
    {
        ResetAddAccountDialogState(clearOfflineName: true);
    }

    public void BackToAddAccountTypeStep()
    {
        if (IsAddAccountDialogBusy)
            return;

        ResetAddAccountDialogState(clearOfflineName: false);
    }

    public void BeginMicrosoftAccountLogin()
    {
        // 先进入 Busy 状态再启动外部浏览器登录，保证按钮状态和状态文案在异步边界前完成切换。
        AddAccountDialogStep = AccountDialogSteps.AddAccountMicrosoftLogin;
        IsAddAccountDialogBusy = true;
        ResetMicrosoftLoginResultState(MicrosoftLoginActiveMessage);
        ReportStatus(Strings.Status_OpeningMicrosoftLogin);
    }

    public async Task CompleteMicrosoftAccountLoginAsync()
    {
        try
        {
            // 服务返回的资料仍需在 UI 边界校验；缺少名称或 UUID 的响应不能进入账户集合。
            var account = await microsoftAccountService.LoginInteractivelyAsync();
            if (string.IsNullOrWhiteSpace(account.DisplayName) || string.IsNullOrWhiteSpace(account.Uuid))
            {
                var message = Strings.Status_LoginMissingProfile;
                ReportStatus(message);
                ShowMicrosoftLoginResult(false, message);
                return;
            }

            // Microsoft UUID 才是稳定身份。重复登录时选中已有账户并刷新顺序，不能创建名称相同的副本。
            var existing = accountList.Accounts.FirstOrDefault(item =>
                item.Account.IsMicrosoft && string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                accountList.SelectItem(existing, persistSelection: false);
                await accountList.PersistAccountOrderAsync();
                var message = string.Format(Strings.Status_LoginAccountAlreadyAddedFormat, existing.DisplayName);
                ReportStatus(message);
                ShowMicrosoftLoginResult(true, message, alreadyAdded: true);
                return;
            }

            await accountList.AddAndSelectAsync(account);
            var addedMessage = string.Format(Strings.Status_LoginAccountAddedFormat, account.DisplayName);
            ReportStatus(addedMessage);
            ShowMicrosoftLoginResult(true, addedMessage);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Microsoft account login canceled.");
            var message = Strings.Status_LoginCanceled;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Microsoft account login failed.");
            var message = Strings.Status_LoginFailed;
            ReportStatus(message);
            ShowMicrosoftLoginResult(false, message);
        }
        finally
        {
            // 所有成功、取消和异常路径都必须解除 Busy，否则对话框会永久失去关闭能力。
            IsAddAccountDialogBusy = false;
        }
    }

    public void CloseAddAccountDialogAfterMicrosoftResult()
    {
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
    }
}
