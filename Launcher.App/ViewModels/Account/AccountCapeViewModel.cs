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
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Account;

public sealed partial class AccountCapeViewModel : ObservableObject
{
    [ObservableProperty]
    private AccountCapeOption? selectedOption;

    public ObservableCollection<AccountCapeOption> Options { get; } = [];

    public AccountCapeOption? PreviousOption => GetAdjacent(-1);

    public AccountCapeOption? NextOption => GetAdjacent(1);

    public bool HasOptions => Options.Count > 0;

    public void SelectPrevious()
    {
        if (PreviousOption is { } option)
            SelectedOption = option;
    }

    public void SelectNext()
    {
        if (NextOption is { } option)
            SelectedOption = option;
    }

    public void NotifyCollectionChanged()
    {
        OnPropertyChanged(nameof(HasOptions));
        NotifyAdjacentChanged();
    }

    partial void OnSelectedOptionChanged(AccountCapeOption? value)
    {
        NotifyAdjacentChanged();
    }

    private AccountCapeOption? GetAdjacent(int offset)
    {
        if (SelectedOption is null || Options.Count < 2)
            return null;
        var index = Options.IndexOf(SelectedOption);
        if (index < 0)
            return null;
        var adjacent = index + offset;
        return adjacent >= 0 && adjacent < Options.Count ? Options[adjacent] : null;
    }

    private void NotifyAdjacentChanged()
    {
        OnPropertyChanged(nameof(PreviousOption));
        OnPropertyChanged(nameof(NextOption));
    }
}
