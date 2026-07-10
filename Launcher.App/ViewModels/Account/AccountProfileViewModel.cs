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

using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountProfileViewModel : ObservableObject, IDisposable
{
    private CancellationTokenSource accountLifetime = new();
    private CancellationTokenSource? currentOperation;
    private string? accountId;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string message = string.Empty;

    [ObservableProperty]
    private string errorCodeMessage = string.Empty;

    public bool HasErrorCode => !string.IsNullOrWhiteSpace(ErrorCodeMessage);

    public void SetAccount(LauncherAccount? account)
    {
        accountId = account?.Id;
        CancelCurrentOperation();
        var next = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref accountLifetime, next);
        previous.Cancel();
        previous.Dispose();
        IsBusy = false;
        ErrorCodeMessage = string.Empty;
    }

    public CancellationToken BeginOperation(string selectedAccountId, string message)
    {
        if (!string.Equals(accountId, selectedAccountId, StringComparison.Ordinal))
            return new CancellationToken(canceled: true);
        IsBusy = true;
        ErrorCodeMessage = string.Empty;
        Message = message;
        CancelCurrentOperation();
        currentOperation = CancellationTokenSource.CreateLinkedTokenSource(accountLifetime.Token);
        return currentOperation.Token;
    }

    public bool IsCurrent(LauncherAccount account)
    {
        return string.Equals(accountId, account.Id, StringComparison.Ordinal)
            && !accountLifetime.IsCancellationRequested;
    }

    public bool IsCurrent(LauncherAccount account, CancellationToken cancellationToken)
    {
        return IsCurrent(account)
            && currentOperation is not null
            && currentOperation.Token == cancellationToken
            && !cancellationToken.IsCancellationRequested;
    }

    public void Complete(LauncherAccount account)
    {
        if (IsCurrent(account))
            IsBusy = false;
    }

    public void Complete(LauncherAccount account, CancellationToken cancellationToken)
    {
        if (!IsCurrent(account, cancellationToken))
            return;
        IsBusy = false;
        CancelCurrentOperation();
    }

    public void SetMessage(string message)
    {
        Message = message;
    }

    public void SetError(Exception exception, string message)
    {
        Message = message;
        ErrorCodeMessage = AccountErrorCodeMessageFormatter.Format(exception);
    }

    public void Dispose()
    {
        CancelCurrentOperation();
        accountLifetime.Cancel();
        accountLifetime.Dispose();
    }

    partial void OnErrorCodeMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasErrorCode));
    }

    private void CancelCurrentOperation()
    {
        var operation = Interlocked.Exchange(ref currentOperation, null);
        if (operation is null)
            return;
        operation.Cancel();
        operation.Dispose();
    }
}
