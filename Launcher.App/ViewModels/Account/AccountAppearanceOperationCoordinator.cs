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

internal sealed partial class AccountAppearanceOperationCoordinator : ObservableObject, IDisposable
{
    private CancellationTokenSource accountLifetime = new();
    private CancellationTokenSource? currentOperation;
    private string? accountId;
    private long generation;

    [ObservableProperty]
    private bool isBusy;

    public void SetAccount(LauncherAccount? account)
    {
        accountId = account?.Id;
        Interlocked.Increment(ref generation);
        var previousLifetime = Interlocked.Exchange(ref accountLifetime, new CancellationTokenSource());
        previousLifetime.Cancel();
        previousLifetime.Dispose();
        CancelCurrentOperation();
        IsBusy = false;
    }

    public AccountAppearanceOperation Begin(LauncherAccount account)
    {
        if (!string.Equals(account.Id, accountId, StringComparison.Ordinal))
            SetAccount(account);
        CancelCurrentOperation();
        var operation = CancellationTokenSource.CreateLinkedTokenSource(accountLifetime.Token);
        currentOperation = operation;
        IsBusy = true;
        return new AccountAppearanceOperation(account.Id, generation, operation, operation.Token);
    }

    public bool IsCurrent(LauncherAccount account, AccountAppearanceOperation operation)
    {
        return !operation.Token.IsCancellationRequested
            && operation.Generation == generation
            && string.Equals(account.Id, accountId, StringComparison.Ordinal)
            && ReferenceEquals(operation.Source, currentOperation);
    }

    public void Complete(LauncherAccount account, AccountAppearanceOperation operation)
    {
        if (!IsCurrent(account, operation))
            return;
        currentOperation = null;
        operation.Source.Dispose();
        IsBusy = false;
    }

    public void Dispose()
    {
        CancelCurrentOperation();
        accountLifetime.Cancel();
        accountLifetime.Dispose();
    }

    private void CancelCurrentOperation()
    {
        var operation = Interlocked.Exchange(ref currentOperation, null);
        operation?.Cancel();
        operation?.Dispose();
    }
}

internal readonly record struct AccountAppearanceOperation(
    string AccountId,
    long Generation,
    CancellationTokenSource Source,
    CancellationToken Token);
