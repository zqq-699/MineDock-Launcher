/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.Globalization;
using Launcher.App.Converters;
using Launcher.App.Resources;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests.ViewModels.Account;

public sealed class AccountKindTextConverterTests
{
    [Fact]
    public void ThirdPartyAccountIncludesPlatformNameWhenAvailable()
    {
        var converter = new AccountKindTextConverter();
        var account = new LauncherAccount
        {
            Id = "third-party",
            DisplayName = "Player",
            Kind = LauncherAccountKind.ThirdParty,
            ThirdPartyPlatformName = "LittleSkin"
        };

        var text = converter.Convert(account, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal($"{Strings.Account_TypeThirdPartyTitle} · LittleSkin", text);
    }

    [Fact]
    public void ThirdPartyAccountWithoutPlatformKeepsOriginalTypeText()
    {
        var converter = new AccountKindTextConverter();
        var account = new LauncherAccount
        {
            Id = "third-party",
            DisplayName = "Player",
            Kind = LauncherAccountKind.ThirdParty
        };

        var text = converter.Convert(account, typeof(string), null!, CultureInfo.InvariantCulture);

        Assert.Equal(Strings.Account_TypeThirdPartyTitle, text);
    }
}
