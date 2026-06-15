using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.App.Models;
using Launcher.App.Resources;
using Launcher.App.Services;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels;

public sealed partial class AccountOfflineUuidViewModel : ObservableObject
{
    private readonly AccountListViewModel accountList;
    private readonly IOfflineAccountUuidService offlineUuidService;
    private readonly IStatusService statusService;
    private bool isRefreshingSelection;

    [ObservableProperty]
    private OfflineUuidModeOption? selectedOfflineUuidOption;

    [ObservableProperty]
    private string manualUuidText = string.Empty;

    [ObservableProperty]
    private bool isManualUuidInvalid;

    public AccountOfflineUuidViewModel(
        AccountListViewModel accountList,
        IOfflineAccountUuidService offlineUuidService,
        IStatusService statusService)
    {
        this.accountList = accountList;
        this.offlineUuidService = offlineUuidService;
        this.statusService = statusService;

        OfflineUuidOptions = new ObservableCollection<OfflineUuidModeOption>(
        [
            new()
            {
                Mode = OfflineUuidGenerationMode.Standard,
                Title = Strings.Account_OfflineUuidStandardTitle,
                Description = Strings.Account_OfflineUuidStandardDescription
            },
            new()
            {
                Mode = OfflineUuidGenerationMode.Random,
                Title = Strings.Account_OfflineUuidRandomTitle,
                Description = Strings.Account_OfflineUuidRandomDescription
            },
            new()
            {
                Mode = OfflineUuidGenerationMode.Manual,
                Title = Strings.Account_OfflineUuidManualTitle,
                Description = Strings.Account_OfflineUuidManualDescription
            }
        ]);

        accountList.PropertyChanged += AccountList_PropertyChanged;
        RefreshSelection();
    }

    public ObservableCollection<OfflineUuidModeOption> OfflineUuidOptions { get; }

    public bool HasSelectedOfflineAccount => accountList.SelectedAccount?.IsOffline == true;

    public bool HasManualUuidEditor =>
        HasSelectedOfflineAccount && SelectedOfflineUuidOption?.Mode == OfflineUuidGenerationMode.Manual;

    public bool CanApplyManualUuid => HasManualUuidEditor && !string.IsNullOrWhiteSpace(ManualUuidText);

    public string SelectedAccountUuidText
    {
        get
        {
            var uuid = accountList.SelectedAccount?.Uuid;
            return string.IsNullOrWhiteSpace(uuid) ? Strings.Account_NoneValue : uuid;
        }
    }

    partial void OnSelectedOfflineUuidOptionChanged(OfflineUuidModeOption? value)
    {
        if (isRefreshingSelection || value is null)
            return;

        IsManualUuidInvalid = false;
        OnPropertyChanged(nameof(HasManualUuidEditor));
        OnPropertyChanged(nameof(CanApplyManualUuid));
        ApplyManualUuidCommand.NotifyCanExecuteChanged();

        if (value.Mode == OfflineUuidGenerationMode.Manual)
        {
            ManualUuidText = accountList.SelectedAccount?.Uuid ?? string.Empty;
            return;
        }

        _ = SelectOfflineUuidModeAsync(value);
    }

    partial void OnManualUuidTextChanged(string value)
    {
        IsManualUuidInvalid = false;
        OnPropertyChanged(nameof(CanApplyManualUuid));
        ApplyManualUuidCommand.NotifyCanExecuteChanged();
    }

    private async Task SelectOfflineUuidModeAsync(OfflineUuidModeOption option)
    {
        LauncherAccount? originalAccount = null;
        try
        {
            var account = accountList.SelectedAccount;
            if (account is null || !account.IsOffline)
                return;

            originalAccount = account;
            var existingUuid = account.OfflineUuidGenerationMode == option.Mode
                ? account.Uuid
                : null;
            var uuid = offlineUuidService.CreateUuid(account.DisplayName, option.Mode, existingUuid);
            var updatedAccount = AccountMapper.WithOfflineUuid(account, option.Mode, uuid);

            accountList.ReplaceSelectedAccount(account, updatedAccount);
            await accountList.PersistAccountOrderAsync();
            statusService.Report(string.Format(Strings.Status_OfflineUuidModeChangedFormat, option.Title));
        }
        catch (Exception)
        {
            var currentAccount = accountList.SelectedAccount;
            if (originalAccount is not null
                && currentAccount is not null
                && string.Equals(currentAccount.Id, originalAccount.Id, StringComparison.Ordinal))
            {
                accountList.ReplaceSelectedAccount(currentAccount, originalAccount);
            }

            RefreshSelection();
            statusService.Report(Strings.Status_OfflineUuidModeChangeFailed);
        }
    }

    [RelayCommand(CanExecute = nameof(CanApplyManualUuid))]
    private async Task ApplyManualUuidAsync()
    {
        var account = accountList.SelectedAccount;
        if (account is null || !account.IsOffline)
            return;

        if (!offlineUuidService.TryNormalizeUuid(ManualUuidText, out var uuid))
        {
            IsManualUuidInvalid = true;
            statusService.Report(Strings.Status_OfflineUuidInvalid);
            return;
        }

        try
        {
            var updatedAccount = AccountMapper.WithOfflineUuid(
                account,
                OfflineUuidGenerationMode.Manual,
                uuid);

            accountList.ReplaceSelectedAccount(account, updatedAccount);
            await accountList.PersistAccountOrderAsync();
            ManualUuidText = uuid;
            statusService.Report(Strings.Status_OfflineUuidApplied);
        }
        catch (Exception)
        {
            var currentAccount = accountList.SelectedAccount;
            if (currentAccount is not null
                && string.Equals(currentAccount.Id, account.Id, StringComparison.Ordinal))
            {
                accountList.ReplaceSelectedAccount(currentAccount, account);
            }

            RefreshSelection();
            statusService.Report(Strings.Status_OfflineUuidModeChangeFailed);
        }
    }

    private void RefreshSelection()
    {
        isRefreshingSelection = true;
        try
        {
            var account = accountList.SelectedAccount;
            SelectedOfflineUuidOption = account is { IsOffline: true }
                ? OfflineUuidOptions.FirstOrDefault(option => option.Mode == account.OfflineUuidGenerationMode)
                : null;
            ManualUuidText = account?.Uuid ?? string.Empty;
            IsManualUuidInvalid = false;
        }
        finally
        {
            isRefreshingSelection = false;
        }

        OnPropertyChanged(nameof(HasSelectedOfflineAccount));
        OnPropertyChanged(nameof(HasManualUuidEditor));
        OnPropertyChanged(nameof(CanApplyManualUuid));
        OnPropertyChanged(nameof(SelectedAccountUuidText));
        ApplyManualUuidCommand.NotifyCanExecuteChanged();
    }

    private void AccountList_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountListViewModel.SelectedAccount))
            RefreshSelection();
    }
}
