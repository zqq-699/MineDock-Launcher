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

using System.Globalization;
using System.Windows.Data;
using Launcher.Application.Accounts;
using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.Converters;

public sealed class AccountKindTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var account = value as LauncherAccount;
        var kind = account?.Kind ?? (value is LauncherAccountKind accountKind ? accountKind : (LauncherAccountKind?)null);
        if (account?.IsThirdParty == true && !string.IsNullOrWhiteSpace(account.ThirdPartyPlatformName))
        {
            return string.Format(
                culture,
                Strings.Account_ThirdPartyPlatformFormat,
                Strings.Account_TypeThirdPartyTitle,
                account.ThirdPartyPlatformName.Trim());
        }

        return kind is not null
            ? kind switch
            {
                LauncherAccountKind.Offline => Strings.Account_TypeOfflineTitle,
                LauncherAccountKind.Microsoft => Strings.Account_TypeMicrosoftTitle,
                LauncherAccountKind.ThirdParty => Strings.Account_TypeThirdPartyTitle,
                _ => string.Empty
            }
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
