/* BlockHelm Launcher - SPDX-License-Identifier: GPL-3.0-only */
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Application.Accounts;

namespace Launcher.App.ViewModels.Account;

public sealed partial class ThirdPartyProfileOptionViewModel(ThirdPartyProfileOption profile) : ObservableObject
{
    public ThirdPartyProfileOption Profile { get; } = profile;
    public string Uuid => Profile.Uuid;
    public string Name => Profile.Name;
    public string AvatarSource => Profile.AvatarSource;

    [ObservableProperty]
    private bool isSelected;
}
