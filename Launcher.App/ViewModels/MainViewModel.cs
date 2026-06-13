using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Services;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService settingsService;
    private readonly IGameVersionService gameVersionService;
    private readonly IGameInstanceService instanceService;
    private readonly ILaunchService launchService;
    private readonly IModService modService;
    private readonly IModrinthService modrinthService;
    private readonly IMicrosoftAccountService microsoftAccountService;
    private readonly IReadOnlyDictionary<LoaderKind, ILoaderProvider> loaderProviders;

    [ObservableProperty]
    private LauncherSettings settings = new();

    [ObservableProperty]
    private string currentPage = "Home";

    [ObservableProperty]
    private bool isMenuExpanded;

    [ObservableProperty]
    private string statusMessage = "准备就绪";

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private GameInstance? selectedInstance;

    [ObservableProperty]
    private MinecraftVersionInfo? selectedMinecraftVersion;

    [ObservableProperty]
    private LoaderKind selectedLoader = LoaderKind.Vanilla;

    [ObservableProperty]
    private LoaderVersionInfo? selectedLoaderVersion;

    [ObservableProperty]
    private string newInstanceName = string.Empty;

    [ObservableProperty]
    private string modSearchQuery = string.Empty;

    [ObservableProperty]
    private ModrinthProject? selectedModrinthProject;

    [ObservableProperty]
    private LauncherAccount? selectedAccount;

    [ObservableProperty]
    private bool isAddAccountDialogOpen;

    [ObservableProperty]
    private bool isAddAccountDialogBusy;

    [ObservableProperty]
    private AccountTypeOption? selectedAccountTypeOption;

    [ObservableProperty]
    private string addAccountDialogStep = "Type";

    [ObservableProperty]
    private string newOfflineAccountName = string.Empty;

    [ObservableProperty]
    private bool isNewOfflineAccountNameInvalid;

    [ObservableProperty]
    private string microsoftLoginMessage = "\u6b63\u5728\u6253\u5f00 Microsoft \u5b98\u65b9\u767b\u5f55\u9875\u9762...";

    [ObservableProperty]
    private string microsoftLoginIcon = "\uE895";

    [ObservableProperty]
    private bool isMicrosoftLoginSuccessful;

    [ObservableProperty]
    private bool isMicrosoftAccountAlreadyAdded;

    [ObservableProperty]
    private bool isDeleteAccountDialogOpen;

    [ObservableProperty]
    private LauncherAccount? accountPendingDelete;

    [ObservableProperty]
    private AccountCapeOption? selectedAccountCapeOption;

    [ObservableProperty]
    private bool isAccountProfileBusy;

    [ObservableProperty]
    private string accountProfileMessage = string.Empty;

    [ObservableProperty]
    private bool isRenameAccountDialogOpen;

    [ObservableProperty]
    private bool isRenameAccountDialogBusy;

    [ObservableProperty]
    private LauncherAccount? accountPendingRename;

    [ObservableProperty]
    private string renameAccountDialogStep = "Input";

    [ObservableProperty]
    private string renameAccountName = string.Empty;

    [ObservableProperty]
    private bool isRenameAccountNameInvalid;

    [ObservableProperty]
    private bool isRenameAccountSuccessful;

    [ObservableProperty]
    private string renameAccountMessage = string.Empty;

    [ObservableProperty]
    private string renameAccountIcon = "\uE70F";

    public bool IsAccountTypeStep => AddAccountDialogStep == "Type";
    public bool IsOfflineNameStep => AddAccountDialogStep == "OfflineName";
    public bool IsMicrosoftLoginStep => AddAccountDialogStep == "MicrosoftLogin";
    public bool IsMicrosoftLoginResultStep => AddAccountDialogStep == "MicrosoftResult";
    public bool IsMicrosoftStatusStep => IsMicrosoftLoginStep || IsMicrosoftLoginResultStep;
    public bool CanShowAddAccountBackButton => !IsAddAccountDialogBusy && (IsOfflineNameStep || IsMicrosoftLoginStep);
    public bool CanShowAddAccountCancelButton => !IsAddAccountDialogBusy && !IsMicrosoftLoginResultStep;
    public bool IsAddAccountFooterEnabled => !IsAddAccountDialogBusy;
    public bool CanConfirmAddAccountDialog => !IsAddAccountDialogBusy
        && (IsMicrosoftLoginResultStep || IsOfflineNameStep || (IsAccountTypeStep && SelectedAccountTypeOption is not null));
    public bool IsMicrosoftAccountTypeSelected => IsAccountTypeStep && SelectedAccountTypeOption?.Kind is "Microsoft";
    public bool CanChangeSelectedAccountSkin => SelectedAccount is not null && !SelectedAccount.IsOffline;
    public bool CanEditSelectedMicrosoftAccount => SelectedAccount is not null && !SelectedAccount.IsOffline && !IsAccountProfileBusy;
    public bool CanApplySelectedCape => SelectedAccount is not null && !SelectedAccount.IsOffline && SelectedAccountCapeOption is not null;
    public bool HasSelectedAccountCapes => SelectedAccountCapeOptions.Count > 0;
    public bool IsRenameAccountInputStep => RenameAccountDialogStep == "Input";
    public bool IsRenameAccountStatusStep => RenameAccountDialogStep == "Status";
    public bool IsRenameAccountResultStep => RenameAccountDialogStep == "Result";
    public bool IsRenameAccountMessageStep => IsRenameAccountStatusStep || IsRenameAccountResultStep;
    public bool IsRenameMicrosoftAccount => AccountPendingRename is not null && !AccountPendingRename.IsOffline;
    public bool CanShowRenameAccountCancelButton => !IsRenameAccountDialogBusy && IsRenameAccountInputStep;
    public bool CanConfirmRenameAccountDialog => !IsRenameAccountDialogBusy
        && (IsRenameAccountResultStep || (IsRenameAccountInputStep && !string.IsNullOrWhiteSpace(RenameAccountName)));
    public string RenameAccountDialogTitle => RenameAccountDialogStep switch
    {
        "Status" => "\u6b63\u5728\u4fee\u6539",
        "Result" => IsRenameAccountSuccessful ? "\u4fee\u6539\u6210\u529f" : "\u4fee\u6539\u5931\u8d25",
        _ => "\u4fee\u6539\u8d26\u6237\u540d"
    };
    public string RenameAccountDialogSubtitle => RenameAccountDialogStep switch
    {
        "Status" => "\u8bf7\u7a0d\u7b49\uff0c\u6b63\u5728\u5904\u7406\u8d26\u6237\u540d\u4fee\u6539\u3002",
        "Result" => "\u70b9\u51fb\u786e\u5b9a\u8fd4\u56de\u8d26\u6237\u8be6\u60c5\u3002",
        _ => IsRenameMicrosoftAccount
            ? "\u6b63\u7248\u8d26\u6237\u6bcf 30 \u5929\u53ef\u6539\u4e00\u6b21\u540d\uff0c\u8c28\u614e\u64cd\u4f5c\u3002"
            : "\u8f93\u5165\u65b0\u7684\u79bb\u7ebf\u8d26\u6237\u540d\u3002"
    };
    public string AddAccountDialogTitle => AddAccountDialogStep switch
    {
        "OfflineName" => "\u79bb\u7ebf\u8d26\u6237",
        "MicrosoftLogin" => "\u6b63\u7248\u767b\u5f55",
        "MicrosoftResult" => IsMicrosoftAccountAlreadyAdded
            ? "\u8d26\u53f7\u5df2\u5b58\u5728"
            : IsMicrosoftLoginSuccessful ? "\u767b\u5f55\u6210\u529f" : "\u767b\u5f55\u672a\u5b8c\u6210",
        _ => "\u6dfb\u52a0\u8d26\u6237"
    };
    public string AddAccountDialogSubtitle => IsOfflineNameStep
        ? "\u8f93\u5165\u8981\u6dfb\u52a0\u7684\u79bb\u7ebf\u8d26\u6237\u540d\u3002"
        : IsMicrosoftLoginStep
        ? "\u8bf7\u5728\u5f39\u51fa\u7684 Microsoft \u5b98\u65b9\u9875\u9762\u5b8c\u6210\u767b\u5f55\u3002"
        : IsMicrosoftLoginResultStep
        ? "\u70b9\u51fb\u786e\u5b9a\u8fd4\u56de\u8d26\u6237\u5217\u8868\u3002"
        : "\u9009\u62e9\u8981\u6dfb\u52a0\u7684\u8d26\u6237\u7c7b\u578b\u3002";

    public ObservableCollection<NavigationItem> NavigationItems { get; } =
    [
        new() { Page = "Account", Title = "\u8d26\u6237", Icon = "\uE77B" },
        new() { Page = "Home", Title = "主页", Icon = "\uE80F" },
        new() { Page = "Download", Title = "游戏下载", Icon = "\uE896" },
        new() { Page = "GameSettings", Title = "游戏设置", Icon = "\uE713" },
        new() { Page = "Resources", Title = "资源中心", Icon = "\uE8F1" },
        new() { Page = "Settings", Title = "设置", Icon = "\uE713" }
    ];

    public ObservableCollection<NavigationItem> SecondaryItems { get; } = [];
    public ObservableCollection<LauncherAccount> Accounts { get; } = [];
    public ObservableCollection<AccountCapeOption> SelectedAccountCapeOptions { get; } = [];
    public ObservableCollection<AccountTypeOption> AccountTypeOptions { get; } =
    [
        new()
        {
            Kind = "Offline",
            Title = "\u79bb\u7ebf\u8d26\u6237",
            Description = "\u4f7f\u7528\u672c\u5730\u7528\u6237\u540d\u8fdb\u5165\u6e38\u620f\u3002",
            Icon = "\uE77B"
        },
        new()
        {
            Kind = "Microsoft",
            Title = "\u6b63\u7248\u8d26\u6237",
            Description = "\u901a\u8fc7 Microsoft \u8d26\u6237\u767b\u5f55\u5e76\u83b7\u53d6\u76ae\u80a4\u5934\u50cf\u3002",
            Icon = "\uE72E"
        }
    ];
    public ObservableCollection<GameInstance> Instances { get; } = [];
    public ObservableCollection<MinecraftVersionInfo> MinecraftVersions { get; } = [];
    public ObservableCollection<NavigationItem> LoaderItems { get; } = [];
    public ObservableCollection<LoaderVersionInfo> LoaderVersions { get; } = [];
    public ObservableCollection<LocalMod> Mods { get; } = [];
    public ObservableCollection<ModrinthProject> ModrinthProjects { get; } = [];

    public MainViewModel(
        ISettingsService settingsService,
        IGameVersionService gameVersionService,
        IGameInstanceService instanceService,
        ILaunchService launchService,
        IModService modService,
        IModrinthService modrinthService,
        IMicrosoftAccountService microsoftAccountService,
        IEnumerable<ILoaderProvider> loaderProviders)
    {
        this.settingsService = settingsService;
        this.gameVersionService = gameVersionService;
        this.instanceService = instanceService;
        this.launchService = launchService;
        this.modService = modService;
        this.modrinthService = modrinthService;
        this.microsoftAccountService = microsoftAccountService;
        this.loaderProviders = loaderProviders.ToDictionary(provider => provider.Kind);

        foreach (var provider in this.loaderProviders.Values)
        {
            LoaderItems.Add(new NavigationItem
            {
                Page = provider.Kind.ToString(),
                Title = provider.DisplayName,
                Icon = provider.Kind is LoaderKind.Vanilla ? "\uE7C3" : "\uE8B7",
                Loader = provider.Kind
            });
        }

        UpdateNavigationSelection();
    }

    [RelayCommand]
    public async Task InitializeAsync()
    {
        UpdateNavigationSelection();
        Settings = await settingsService.LoadAsync();
        IsMenuExpanded = Settings.IsMenuExpanded;
        await LoadAccountsFromSettingsAsync();
        await RefreshInstancesAsync();
        await LoadMinecraftVersionsAsync();
        UpdateSecondaryItems();
        UpdateNavigationSelection();
    }

    [RelayCommand]
    private void Navigate(NavigationItem item)
    {
        SelectNavigationItem(item);
    }

    public void SelectNavigationItem(NavigationItem item)
    {
        if (item.Loader is LoaderKind loader)
        {
            SelectedLoader = loader;
            CurrentPage = "Download";
        }
        else
        {
            CurrentPage = item.Page;
        }

        UpdateSecondaryItems();
        UpdateNavigationSelection();
    }

    public void SelectAccount(LauncherAccount account)
    {
        IsAccountProfileBusy = false;
        SelectedAccount = account;
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, account);

        UpdateAccountNavigationAvatar();
        ResetSelectedAccountProfileState(account);
    }

    public async Task ChangeSelectedAccountSkinAsync(string skinFilePath)
    {
        var account = SelectedAccount;
        if (account is null)
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = "\u53ea\u6709\u6b63\u7248\u8d26\u6237\u53ef\u4ee5\u66f4\u6362\u76ae\u80a4";
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = "\u6b63\u5728\u4e0a\u4f20\u76ae\u80a4...";
            var updatedAccount = await microsoftAccountService.UploadSkinAsync(account, skinFilePath);
            ReplaceSelectedAccount(account, updatedAccount);
            await PersistAccountOrderAsync();
            AccountProfileMessage = "\u76ae\u80a4\u5df2\u66f4\u65b0";
            await LoadSelectedAccountProfileAsync(updatedAccount);
        }
        catch (Exception ex)
        {
            AccountProfileMessage = $"\u66f4\u6362\u76ae\u80a4\u5931\u8d25\uff1a{ex.Message}";
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    public async Task RefreshSelectedAccountProfileAsync()
    {
        if (SelectedAccount is not null)
            await LoadSelectedAccountProfileAsync(SelectedAccount);
    }

    public async Task ApplySelectedAccountCapeAsync()
    {
        var account = SelectedAccount;
        var cape = SelectedAccountCapeOption;
        if (account is null || cape is null)
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = "\u53ea\u6709\u6b63\u7248\u8d26\u6237\u53ef\u4ee5\u66f4\u6362\u62ab\u98ce";
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = "\u6b63\u5728\u66f4\u6362\u62ab\u98ce...";
            await microsoftAccountService.SetActiveCapeAsync(account, cape.Id);
            AccountProfileMessage = cape.IsNone ? "\u5df2\u79fb\u9664\u5f53\u524d\u62ab\u98ce" : $"\u5df2\u66f4\u6362\u62ab\u98ce\uff1a{cape.DisplayName}";
            MarkSelectedCapeActive(cape);
            await StoreSelectedAccountCapeCacheAsync();
        }
        catch (Exception ex)
        {
            AccountProfileMessage = $"\u66f4\u6362\u62ab\u98ce\u5931\u8d25\uff1a{ex.Message}";
        }
        finally
        {
            IsAccountProfileBusy = false;
        }
    }

    public void OpenAddAccountDialog()
    {
        AddAccountDialogStep = "Type";
        NewOfflineAccountName = string.Empty;
        IsNewOfflineAccountNameInvalid = false;
        IsAddAccountDialogBusy = false;
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = "\uE895";
        MicrosoftLoginMessage = "\u6b63\u5728\u6253\u5f00 Microsoft \u5b98\u65b9\u767b\u5f55\u9875\u9762...";
        SelectedAccountTypeOption = null;
        IsAddAccountDialogOpen = true;
    }

    public void CancelAddAccountDialog()
    {
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
    }

    public void ResetAddAccountDialog()
    {
        AddAccountDialogStep = "Type";
        NewOfflineAccountName = string.Empty;
        IsNewOfflineAccountNameInvalid = false;
        IsAddAccountDialogBusy = false;
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = "\uE895";
        MicrosoftLoginMessage = "\u6b63\u5728\u6253\u5f00 Microsoft \u5b98\u65b9\u767b\u5f55\u9875\u9762...";
        SelectedAccountTypeOption = null;
    }

    public void BackToAddAccountTypeStep()
    {
        if (IsAddAccountDialogBusy)
            return;

        AddAccountDialogStep = "Type";
        IsNewOfflineAccountNameInvalid = false;
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = "\uE895";
        MicrosoftLoginMessage = "\u6b63\u5728\u6253\u5f00 Microsoft \u5b98\u65b9\u767b\u5f55\u9875\u9762...";
        SelectedAccountTypeOption = null;
    }

    public void BeginMicrosoftAccountLogin()
    {
        AddAccountDialogStep = "MicrosoftLogin";
        IsAddAccountDialogBusy = true;
        IsMicrosoftLoginSuccessful = false;
        IsMicrosoftAccountAlreadyAdded = false;
        MicrosoftLoginIcon = "\uE895";
        MicrosoftLoginMessage = "\u6b63\u5728\u767b\u5f55\uff0c\u8bf7\u5728\u5f39\u51fa\u7684 Microsoft \u5b98\u65b9\u9875\u9762\u5b8c\u6210\u767b\u5f55...";
        StatusMessage = "\u6b63\u5728\u6253\u5f00 Microsoft \u767b\u5f55\u9875\u9762...";
    }

    public async Task CompleteMicrosoftAccountLoginAsync()
    {
        try
        {
            var account = await microsoftAccountService.LoginInteractivelyAsync();
            if (string.IsNullOrWhiteSpace(account.DisplayName) || string.IsNullOrWhiteSpace(account.Uuid))
            {
                StatusMessage = "\u6b63\u7248\u767b\u5f55\u5931\u8d25\uff1a\u672a\u83b7\u53d6\u5230 Minecraft Java \u8d26\u6237\u8d44\u6599";
                ShowMicrosoftLoginResult(false, StatusMessage);
                return;
            }

            var existing = Accounts.FirstOrDefault(item =>
                !item.IsOffline && string.Equals(item.Uuid, account.Uuid, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                SelectAccount(existing);
                await PersistAccountOrderAsync();
                StatusMessage = $"\u6b63\u7248\u8d26\u6237 {existing.DisplayName} \u5df2\u7ecf\u6dfb\u52a0\u8fc7\u4e86\uff0c\u5df2\u4e3a\u4f60\u9009\u4e2d";
                ShowMicrosoftLoginResult(true, StatusMessage, alreadyAdded: true);
                return;
            }

            Accounts.Add(account);
            SelectAccount(account);
            await PersistAccountOrderAsync();
            StatusMessage = $"\u5df2\u6dfb\u52a0\u6b63\u7248\u8d26\u6237 {account.DisplayName}";
            ShowMicrosoftLoginResult(true, StatusMessage);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "\u6b63\u7248\u767b\u5f55\u5df2\u53d6\u6d88";
            ShowMicrosoftLoginResult(false, StatusMessage);
        }
        catch (Exception ex)
        {
            StatusMessage = $"\u6b63\u7248\u767b\u5f55\u5931\u8d25\uff1a{ex.Message}";
            ShowMicrosoftLoginResult(false, StatusMessage);
        }
        finally
        {
            IsAddAccountDialogBusy = false;
        }
    }

    public void CloseAddAccountDialogAfterMicrosoftResult()
    {
        if (IsAddAccountDialogBusy)
            return;

        IsAddAccountDialogOpen = false;
    }

    public async Task ConfirmAddAccountDialogAsync()
    {
        if (IsAddAccountDialogBusy)
            return;

        if (IsMicrosoftLoginResultStep)
        {
            CloseAddAccountDialogAfterMicrosoftResult();
            return;
        }

        if (SelectedAccountTypeOption is null)
            return;

        if (IsAccountTypeStep)
        {
            if (SelectedAccountTypeOption.Kind is "Offline")
            {
                AddAccountDialogStep = "OfflineName";
                return;
            }

            return;
        }

        var accountName = NewOfflineAccountName.Trim();
        if (string.IsNullOrWhiteSpace(accountName))
        {
            IsNewOfflineAccountNameInvalid = true;
            StatusMessage = "\u8bf7\u8f93\u5165\u79bb\u7ebf\u8d26\u6237\u540d";
            return;
        }

        var account = new LauncherAccount
        {
            Id = $"offline-{Guid.NewGuid():N}",
            DisplayName = accountName,
            IsOffline = true
        };

        Accounts.Add(account);
        SelectAccount(account);
        await PersistAccountOrderAsync();

        IsAddAccountDialogOpen = false;
        StatusMessage = $"\u5df2\u6dfb\u52a0\u79bb\u7ebf\u8d26\u6237 {accountName}";
    }

    public void OpenDeleteAccountDialog(LauncherAccount account)
    {
        AccountPendingDelete = account;
        IsDeleteAccountDialogOpen = true;
    }

    public void CancelDeleteAccountDialog()
    {
        IsDeleteAccountDialogOpen = false;
        AccountPendingDelete = null;
    }

    public async Task ConfirmDeleteAccountDialogAsync()
    {
        if (AccountPendingDelete is null)
            return;

        var account = AccountPendingDelete;
        var deletedName = account.DisplayName;

        if (ReferenceEquals(SelectedAccount, account))
        {
            SelectedAccount = null;
            UpdateAccountNavigationAvatar();
        }

        Accounts.Remove(account);
        IsDeleteAccountDialogOpen = false;
        AccountPendingDelete = null;
        StatusMessage = $"\u5df2\u5220\u9664\u8d26\u6237 {deletedName}";

        try
        {
            await PersistAccountOrderAsync();
            if (!account.IsOffline)
                await microsoftAccountService.DeleteAccountAsync(account);
        }
        catch (Exception ex)
        {
            StatusMessage = $"\u5df2\u4ece\u5217\u8868\u5220\u9664\u8d26\u6237 {deletedName}\uff0c\u4f46\u6e05\u7406\u767b\u5f55\u7f13\u5b58\u5931\u8d25\uff1a{ex.Message}";
        }
    }

    public void OpenRenameAccountDialog()
    {
        if (SelectedAccount is null)
            return;

        AccountPendingRename = SelectedAccount;
        RenameAccountDialogStep = "Input";
        RenameAccountName = SelectedAccount.DisplayName;
        IsRenameAccountNameInvalid = false;
        IsRenameAccountDialogBusy = false;
        IsRenameAccountSuccessful = false;
        RenameAccountIcon = "\uE70F";
        RenameAccountMessage = string.Empty;
        IsRenameAccountDialogOpen = true;
    }

    public void CancelRenameAccountDialog()
    {
        if (IsRenameAccountDialogBusy)
            return;

        IsRenameAccountDialogOpen = false;
    }

    public void ResetRenameAccountDialog()
    {
        IsRenameAccountDialogBusy = false;
        AccountPendingRename = null;
        RenameAccountDialogStep = "Input";
        RenameAccountName = string.Empty;
        IsRenameAccountNameInvalid = false;
        IsRenameAccountSuccessful = false;
        RenameAccountIcon = "\uE70F";
        RenameAccountMessage = string.Empty;
    }

    public async Task ConfirmRenameAccountDialogAsync()
    {
        if (IsRenameAccountDialogBusy)
            return;

        if (IsRenameAccountResultStep)
        {
            IsRenameAccountDialogOpen = false;
            return;
        }

        var account = AccountPendingRename;
        if (account is null)
            return;

        var newName = RenameAccountName.Trim();
        if (string.IsNullOrWhiteSpace(newName))
        {
            IsRenameAccountNameInvalid = true;
            return;
        }

        if (string.Equals(newName, account.DisplayName, StringComparison.Ordinal))
        {
            ShowRenameAccountResult(true, "\u8d26\u6237\u540d\u6ca1\u6709\u53d8\u5316");
            return;
        }

        try
        {
            IsRenameAccountDialogBusy = true;
            RenameAccountDialogStep = "Status";
            RenameAccountIcon = "\uE895";
            RenameAccountMessage = account.IsOffline
                ? "\u6b63\u5728\u4fdd\u5b58\u79bb\u7ebf\u8d26\u6237\u540d..."
                : "\u6b63\u5728\u8054\u7f51\u4fee\u6539\u6b63\u7248\u8d26\u6237\u540d...";

            LauncherAccount updatedAccount;
            if (account.IsOffline)
            {
                updatedAccount = CopyAccountWithName(account, newName);
            }
            else
            {
                var renamedAccount = await microsoftAccountService.ChangeNameAsync(account, newName);
                updatedAccount = CopyAccountWithCapeCache(renamedAccount, account.CachedCapeOptions);
            }

            ReplaceSelectedAccount(account, updatedAccount);
            AccountPendingRename = updatedAccount;
            await PersistAccountOrderAsync();
            StatusMessage = $"\u5df2\u5c06\u8d26\u6237\u540d\u4fee\u6539\u4e3a {updatedAccount.DisplayName}";
            ShowRenameAccountResult(true, $"\u8d26\u6237\u540d\u5df2\u4fee\u6539\u4e3a {updatedAccount.DisplayName}");
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            ShowRenameAccountResult(false, ex.Message);
        }
        finally
        {
            IsRenameAccountDialogBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleMenuAsync()
    {
        IsMenuExpanded = !IsMenuExpanded;
        Settings.IsMenuExpanded = IsMenuExpanded;
        await settingsService.SaveAsync(Settings);
    }

    [RelayCommand]
    private async Task LoadMinecraftVersionsAsync()
    {
        StatusMessage = "正在获取 Minecraft 版本列表...";
        MinecraftVersions.Clear();

        foreach (var version in await gameVersionService.GetVersionsAsync())
        {
            if (version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase) || version.Name.StartsWith("1."))
                MinecraftVersions.Add(version);
        }

        SelectedMinecraftVersion ??= MinecraftVersions.FirstOrDefault();
        StatusMessage = $"已加载 {MinecraftVersions.Count} 个版本";
    }

    [RelayCommand]
    private async Task RefreshInstancesAsync()
    {
        Instances.Clear();
        foreach (var instance in await instanceService.GetInstancesAsync())
            Instances.Add(instance);

        SelectedInstance = await instanceService.GetDefaultInstanceAsync() ?? Instances.FirstOrDefault();
        await RefreshModsAsync();
    }

    [RelayCommand]
    private async Task LoadLoaderVersionsAsync()
    {
        LoaderVersions.Clear();
        SelectedLoaderVersion = null;

        if (SelectedMinecraftVersion is null || !loaderProviders.TryGetValue(SelectedLoader, out var provider))
            return;

        if (!provider.IsImplemented)
        {
            StatusMessage = $"{provider.DisplayName} 后续版本接入";
            return;
        }

        StatusMessage = $"正在读取 {provider.DisplayName} 版本...";
        foreach (var version in await provider.GetLoaderVersionsAsync(SelectedMinecraftVersion.Name))
            LoaderVersions.Add(version);

        SelectedLoaderVersion = LoaderVersions.FirstOrDefault(v => v.IsStable) ?? LoaderVersions.FirstOrDefault();
        StatusMessage = $"{provider.DisplayName} 可用版本已加载";
    }

    [RelayCommand]
    private async Task CreateInstanceAsync()
    {
        if (SelectedMinecraftVersion is null)
        {
            StatusMessage = "请先选择 Minecraft 版本";
            return;
        }

        var progress = CreateProgress();
        var loaderVersion = SelectedLoader is LoaderKind.Vanilla ? null : SelectedLoaderVersion?.Version;
        var instance = await instanceService.CreateInstanceAsync(
            SelectedMinecraftVersion.Name,
            SelectedLoader,
            loaderVersion,
            NewInstanceName,
            progress);

        Instances.Add(instance);
        SelectedInstance = instance;
        StatusMessage = $"实例 {instance.Name} 已创建";
    }

    [RelayCommand]
    private async Task LaunchAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "还没有可启动的实例";
            return;
        }

        await launchService.LaunchAsync(SelectedInstance, Settings, CreateProgress());
    }

    [RelayCommand]
    private async Task RefreshModsAsync()
    {
        Mods.Clear();
        if (SelectedInstance is null)
            return;

        foreach (var mod in await modService.GetModsAsync(SelectedInstance))
            Mods.Add(mod);
    }

    [RelayCommand]
    private async Task ToggleModAsync(LocalMod mod)
    {
        await modService.SetEnabledAsync(mod, !mod.IsEnabled);
        await RefreshModsAsync();
    }

    [RelayCommand]
    private async Task DeleteModAsync(LocalMod mod)
    {
        await modService.DeleteAsync(mod);
        await RefreshModsAsync();
    }

    [RelayCommand]
    private async Task ImportModFromPathAsync(string path)
    {
        if (SelectedInstance is null || string.IsNullOrWhiteSpace(path))
            return;

        await modService.ImportAsync(SelectedInstance, path);
        await RefreshModsAsync();
        StatusMessage = "本地 Mod 已导入";
    }

    [RelayCommand]
    private async Task SearchModsAsync()
    {
        if (SelectedInstance is null)
        {
            StatusMessage = "请先选择一个实例";
            return;
        }

        StatusMessage = "正在搜索 Modrinth...";
        ModrinthProjects.Clear();
        foreach (var project in await modrinthService.SearchModsAsync(ModSearchQuery, SelectedInstance.MinecraftVersion, SelectedInstance.Loader))
            ModrinthProjects.Add(project);

        StatusMessage = $"找到 {ModrinthProjects.Count} 个资源";
    }

    [RelayCommand]
    private async Task InstallSelectedModAsync()
    {
        if (SelectedInstance is null || SelectedModrinthProject is null)
            return;

        await modrinthService.InstallLatestCompatibleAsync(SelectedModrinthProject, SelectedInstance, CreateProgress());
        await RefreshModsAsync();
        StatusMessage = $"{SelectedModrinthProject.Title} 已安装";
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        Settings.IsMenuExpanded = IsMenuExpanded;
        await settingsService.SaveAsync(Settings);
        StatusMessage = "启动器设置已保存";
    }

    [RelayCommand]
    private async Task SaveInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        await instanceService.SaveInstanceAsync(SelectedInstance);
        StatusMessage = "实例设置已保存";
    }

    [RelayCommand]
    private async Task SetDefaultInstanceAsync()
    {
        if (SelectedInstance is null)
            return;

        Settings.DefaultInstanceId = SelectedInstance.Id;
        await settingsService.SaveAsync(Settings);
        StatusMessage = $"{SelectedInstance.Name} 已设为默认实例";
    }

    partial void OnSelectedLoaderChanged(LoaderKind value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnCurrentPageChanged(string value)
    {
        UpdateNavigationSelection();
    }

    partial void OnSelectedMinecraftVersionChanged(MinecraftVersionInfo? value)
    {
        _ = LoadLoaderVersionsAsync();
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        _ = RefreshModsAsync();
    }

    partial void OnAddAccountDialogStepChanged(string value)
    {
        OnPropertyChanged(nameof(IsAccountTypeStep));
        OnPropertyChanged(nameof(IsOfflineNameStep));
        OnPropertyChanged(nameof(IsMicrosoftLoginStep));
        OnPropertyChanged(nameof(IsMicrosoftLoginResultStep));
        OnPropertyChanged(nameof(IsMicrosoftStatusStep));
        OnPropertyChanged(nameof(CanShowAddAccountBackButton));
        OnPropertyChanged(nameof(CanShowAddAccountCancelButton));
        OnPropertyChanged(nameof(IsAddAccountFooterEnabled));
        OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
        OnPropertyChanged(nameof(IsMicrosoftAccountTypeSelected));
        OnPropertyChanged(nameof(AddAccountDialogTitle));
        OnPropertyChanged(nameof(AddAccountDialogSubtitle));
    }

    partial void OnIsAddAccountDialogBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowAddAccountBackButton));
        OnPropertyChanged(nameof(CanShowAddAccountCancelButton));
        OnPropertyChanged(nameof(IsAddAccountFooterEnabled));
        OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
    }

    partial void OnIsMicrosoftLoginSuccessfulChanged(bool value)
    {
        OnPropertyChanged(nameof(AddAccountDialogTitle));
    }

    partial void OnIsMicrosoftAccountAlreadyAddedChanged(bool value)
    {
        OnPropertyChanged(nameof(AddAccountDialogTitle));
    }

    partial void OnSelectedAccountTypeOptionChanged(AccountTypeOption? value)
    {
        OnPropertyChanged(nameof(IsMicrosoftAccountTypeSelected));
        OnPropertyChanged(nameof(CanConfirmAddAccountDialog));
    }

    partial void OnSelectedAccountCapeOptionChanged(AccountCapeOption? value)
    {
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    partial void OnIsAccountProfileBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSelectedMicrosoftAccount));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    partial void OnSelectedAccountChanged(LauncherAccount? value)
    {
        OnPropertyChanged(nameof(CanChangeSelectedAccountSkin));
        OnPropertyChanged(nameof(CanEditSelectedMicrosoftAccount));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    partial void OnRenameAccountDialogStepChanged(string value)
    {
        OnPropertyChanged(nameof(IsRenameAccountInputStep));
        OnPropertyChanged(nameof(IsRenameAccountStatusStep));
        OnPropertyChanged(nameof(IsRenameAccountResultStep));
        OnPropertyChanged(nameof(IsRenameAccountMessageStep));
        OnPropertyChanged(nameof(CanShowRenameAccountCancelButton));
        OnPropertyChanged(nameof(CanConfirmRenameAccountDialog));
        OnPropertyChanged(nameof(RenameAccountDialogTitle));
        OnPropertyChanged(nameof(RenameAccountDialogSubtitle));
    }

    partial void OnIsRenameAccountDialogBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanShowRenameAccountCancelButton));
        OnPropertyChanged(nameof(CanConfirmRenameAccountDialog));
    }

    partial void OnAccountPendingRenameChanged(LauncherAccount? value)
    {
        OnPropertyChanged(nameof(IsRenameMicrosoftAccount));
        OnPropertyChanged(nameof(RenameAccountDialogSubtitle));
    }

    partial void OnIsRenameAccountSuccessfulChanged(bool value)
    {
        OnPropertyChanged(nameof(RenameAccountDialogTitle));
    }

    partial void OnRenameAccountNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            IsRenameAccountNameInvalid = false;

        OnPropertyChanged(nameof(CanConfirmRenameAccountDialog));
    }

    partial void OnNewOfflineAccountNameChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            IsNewOfflineAccountNameInvalid = false;
    }

    private IProgress<LauncherProgress> CreateProgress()
    {
        return new Progress<LauncherProgress>(progress =>
        {
            StatusMessage = progress.Message;
            ProgressPercent = progress.Percent ?? 0;
        });
    }

    private void ShowMicrosoftLoginResult(bool isSuccess, string message, bool alreadyAdded = false)
    {
        IsMicrosoftLoginSuccessful = isSuccess;
        IsMicrosoftAccountAlreadyAdded = alreadyAdded;
        MicrosoftLoginIcon = isSuccess ? "\uE73E" : "\uE783";
        MicrosoftLoginMessage = message;
        AddAccountDialogStep = "MicrosoftResult";
    }

    private void ShowRenameAccountResult(bool isSuccess, string message)
    {
        IsRenameAccountSuccessful = isSuccess;
        RenameAccountIcon = isSuccess ? "\uE73E" : "\uE783";
        RenameAccountMessage = message;
        RenameAccountDialogStep = "Result";
    }

    private void UpdateSecondaryItems()
    {
        SecondaryItems.Clear();

        var items = CurrentPage switch
        {
            "Download" => LoaderItems,
            "GameSettings" =>
            [
                new NavigationItem { Page = "GameSettings", Title = "实例列表", Icon = "\uE8A5" },
                new NavigationItem { Page = "GameSettings", Title = "Java/内存", Icon = "\uE950" },
                new NavigationItem { Page = "GameSettings", Title = "目录管理", Icon = "\uE8B7" }
            ],
            "Resources" =>
            [
                new NavigationItem { Page = "Resources", Title = "Mod", Icon = "\uE8F1" },
                new NavigationItem { Page = "Resources", Title = "光影", Icon = "\uE790" },
                new NavigationItem { Page = "Resources", Title = "地图", Icon = "\uE707" }
            ],
            "Settings" =>
            [
                new NavigationItem { Page = "Settings", Title = "外观主题", Icon = "\uE771" },
                new NavigationItem { Page = "Settings", Title = "默认设置", Icon = "\uE713" },
                new NavigationItem { Page = "Settings", Title = "关于", Icon = "\uE946" }
            ],
            _ => []
        };

        foreach (var item in items)
            SecondaryItems.Add(item);
    }

    private void UpdateNavigationSelection()
    {
        foreach (var item in NavigationItems)
            item.IsSelected = string.Equals(item.Page, CurrentPage, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadAccountsFromSettingsAsync()
    {
        Accounts.Clear();
        var microsoftAccounts = new Dictionary<string, LauncherAccount>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in await microsoftAccountService.GetSavedAccountsAsync())
        {
            if (!microsoftAccounts.ContainsKey(account.Id))
                microsoftAccounts.Add(account.Id, account);
        }

        var shouldImportMicrosoftAccounts = !Settings.MicrosoftAccountsImported;
        var shouldPersistOrder = false;

        foreach (var account in Settings.Accounts)
        {
            if (account.IsOffline)
            {
                Accounts.Add(new LauncherAccount
                {
                    Id = account.Id,
                    DisplayName = account.DisplayName,
                    Uuid = account.Uuid,
                    AvatarSource = account.AvatarSource,
                    IsOffline = true,
                    CachedCapeOptions = ToCapeOptions(account.Capes)
                });
                continue;
            }

            if (microsoftAccounts.Remove(account.Id, out var microsoftAccount))
                Accounts.Add(CopyAccountWithStoredRecord(microsoftAccount, account, ToCapeOptions(account.Capes)));
            else
                shouldPersistOrder = true;
        }

        foreach (var account in microsoftAccounts.Values)
        {
            if (shouldImportMicrosoftAccounts && Accounts.All(item => item.Id != account.Id))
            {
                Accounts.Add(account);
                shouldPersistOrder = true;
            }
        }

        SelectedAccount = null;
        UpdateAccountNavigationAvatar();

        if (shouldPersistOrder || shouldImportMicrosoftAccounts)
            await PersistAccountOrderAsync();
    }

    private async Task PersistAccountOrderAsync(LauncherAccount? excludedAccount = null)
    {
        Settings.AccountsInitialized = true;
        Settings.MicrosoftAccountsImported = true;
        Settings.Accounts = Accounts
            .Where(account => !ReferenceEquals(account, excludedAccount))
            .Select(account => new LauncherAccountRecord
            {
                Id = account.Id,
                DisplayName = account.DisplayName,
                Uuid = account.Uuid,
                AvatarSource = account.AvatarSource,
                IsOffline = account.IsOffline,
                Capes = account.CachedCapeOptions.Select(ToCapeRecord).ToList()
            })
            .ToList();

        var firstOfflineAccount = Settings.Accounts.FirstOrDefault(account => account.IsOffline);
        if (firstOfflineAccount is not null)
            Settings.OfflineUsername = firstOfflineAccount.DisplayName;

        await settingsService.SaveAsync(Settings);
    }

    private async Task LoadSelectedAccountProfileAsync(LauncherAccount account)
    {
        SelectedAccountCapeOptions.Clear();
        SelectedAccountCapeOption = null;
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));

        if (!ReferenceEquals(SelectedAccount, account))
            return;

        if (account.IsOffline)
        {
            AccountProfileMessage = "\u79bb\u7ebf\u8d26\u6237\u4e0d\u652f\u6301\u76ae\u80a4\u548c\u62ab\u98ce\u7ba1\u7406";
            return;
        }

        try
        {
            IsAccountProfileBusy = true;
            AccountProfileMessage = "\u6b63\u5728\u8bfb\u53d6\u6b63\u7248\u8d26\u6237\u8d44\u6599...";
            var capes = await microsoftAccountService.GetCapesAsync(account);
            if (!ReferenceEquals(SelectedAccount, account))
                return;

            foreach (var cape in capes)
                SelectedAccountCapeOptions.Add(cape);

            SelectedAccountCapeOption = SelectedAccountCapeOptions.FirstOrDefault(cape => cape.IsActive)
                ?? SelectedAccountCapeOptions.FirstOrDefault();
            OnPropertyChanged(nameof(HasSelectedAccountCapes));
            OnPropertyChanged(nameof(CanApplySelectedCape));
            await StoreSelectedAccountCapeCacheAsync();
            OnPropertyChanged(nameof(CanApplySelectedCape));
            AccountProfileMessage = SelectedAccountCapeOptions.Count == 0
                ? "\u8fd9\u4e2a\u8d26\u6237\u6ca1\u6709\u53ef\u7528\u62ab\u98ce"
                : "\u6b63\u7248\u8d26\u6237\u8d44\u6599\u5df2\u52a0\u8f7d";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedAccount, account))
                AccountProfileMessage = $"\u8bfb\u53d6\u6b63\u7248\u8d26\u6237\u8d44\u6599\u5931\u8d25\uff1a{ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(SelectedAccount, account))
                IsAccountProfileBusy = false;
        }
    }

    private void ResetSelectedAccountProfileState(LauncherAccount account)
    {
        PopulateSelectedAccountCapeOptions(account.CachedCapeOptions);
        AccountProfileMessage = account.IsOffline
            ? "\u79bb\u7ebf\u8d26\u6237\u4e0d\u652f\u6301\u76ae\u80a4\u548c\u62ab\u98ce\u7ba1\u7406"
            : SelectedAccountCapeOptions.Count > 0
                ? "\u5df2\u52a0\u8f7d\u672c\u5730\u62ab\u98ce\u7f13\u5b58\uff0c\u70b9\u51fb\u5237\u65b0\u53ef\u83b7\u53d6\u6700\u65b0\u72b6\u6001"
                : "\u70b9\u51fb\u5237\u65b0\u8bfb\u53d6\u8fd9\u4e2a\u6b63\u7248\u8d26\u6237\u7684\u62ab\u98ce\u5217\u8868";
    }

    private void PopulateSelectedAccountCapeOptions(IEnumerable<AccountCapeOption> capes)
    {
        SelectedAccountCapeOptions.Clear();
        foreach (var cape in capes)
            SelectedAccountCapeOptions.Add(cape);

        SelectedAccountCapeOption = SelectedAccountCapeOptions.FirstOrDefault(cape => cape.IsActive)
            ?? SelectedAccountCapeOptions.FirstOrDefault();
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    private void MarkSelectedCapeActive(AccountCapeOption activeCape)
    {
        var updatedCapes = SelectedAccountCapeOptions
            .Select(cape => new AccountCapeOption
            {
                Id = cape.Id,
                DisplayName = cape.DisplayName,
                IsNone = cape.IsNone,
                IsActive = cape.IsNone == activeCape.IsNone
                    && string.Equals(cape.Id, activeCape.Id, StringComparison.OrdinalIgnoreCase)
            })
            .ToList();

        SelectedAccountCapeOptions.Clear();
        foreach (var cape in updatedCapes)
            SelectedAccountCapeOptions.Add(cape);

        SelectedAccountCapeOption = SelectedAccountCapeOptions.FirstOrDefault(cape => cape.IsActive);
        OnPropertyChanged(nameof(HasSelectedAccountCapes));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    private async Task StoreSelectedAccountCapeCacheAsync()
    {
        var account = SelectedAccount;
        if (account is null)
            return;

        ReplaceSelectedAccount(account, CopyAccountWithCapeCache(account, SelectedAccountCapeOptions.ToList()));
        await PersistAccountOrderAsync();
    }

    private void ReplaceSelectedAccount(LauncherAccount oldAccount, LauncherAccount newAccount)
    {
        var index = Accounts.IndexOf(oldAccount);
        if (index >= 0)
            Accounts[index] = newAccount;

        SelectedAccount = newAccount;
        foreach (var item in Accounts)
            item.IsSelected = ReferenceEquals(item, newAccount);

        UpdateAccountNavigationAvatar();
        OnPropertyChanged(nameof(CanChangeSelectedAccountSkin));
        OnPropertyChanged(nameof(CanEditSelectedMicrosoftAccount));
        OnPropertyChanged(nameof(CanApplySelectedCape));
    }

    private static LauncherAccount CopyAccountWithCapeCache(
        LauncherAccount account,
        IReadOnlyList<AccountCapeOption> capeOptions)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            Uuid = account.Uuid,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            CachedCapeOptions = capeOptions
        };
    }

    private static LauncherAccount CopyAccountWithName(LauncherAccount account, string displayName)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = displayName,
            Uuid = account.Uuid,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            CachedCapeOptions = account.CachedCapeOptions
        };
    }

    private static LauncherAccount CopyAccountWithStoredRecord(
        LauncherAccount account,
        LauncherAccountRecord record,
        IReadOnlyList<AccountCapeOption> capeOptions)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = string.IsNullOrWhiteSpace(record.DisplayName) ? account.DisplayName : record.DisplayName,
            Uuid = account.Uuid,
            AvatarSource = string.IsNullOrWhiteSpace(record.AvatarSource) ? account.AvatarSource : record.AvatarSource,
            IsOffline = account.IsOffline,
            CachedCapeOptions = capeOptions
        };
    }

    private static List<AccountCapeOption> ToCapeOptions(IEnumerable<LauncherCapeRecord>? records)
    {
        if (records is null)
            return [];

        return records
            .Where(record => record.IsNone || !string.IsNullOrWhiteSpace(record.DisplayName))
            .Select(record => new AccountCapeOption
            {
                Id = record.Id,
                DisplayName = string.IsNullOrWhiteSpace(record.DisplayName)
                    ? "\u4e0d\u4f7f\u7528\u62ab\u98ce"
                    : record.DisplayName,
                IsActive = record.IsActive,
                IsNone = record.IsNone
            })
            .ToList();
    }

    private static LauncherCapeRecord ToCapeRecord(AccountCapeOption cape)
    {
        return new LauncherCapeRecord
        {
            Id = cape.Id,
            DisplayName = cape.DisplayName,
            IsActive = cape.IsActive,
            IsNone = cape.IsNone
        };
    }

    private void UpdateAccountNavigationAvatar()
    {
        var accountItem = NavigationItems.FirstOrDefault(item => item.Page == "Account");
        if (accountItem is not null)
            accountItem.AvatarUrl = SelectedAccount?.AvatarUrl;
    }
}
