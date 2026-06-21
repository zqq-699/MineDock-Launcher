using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Account;

public sealed class AccountDetailsViewModel : ObservableObject
{
    private readonly AccountPageViewModel parent;

    public AccountDetailsViewModel(AccountPageViewModel parent)
    {
        this.parent = parent;
        parent.PropertyChanged += OnParentPropertyChanged;
        parent.AccountList.PropertyChanged += OnChildPropertyChanged;
        parent.Appearance.PropertyChanged += OnChildPropertyChanged;
        parent.OfflineUuid.PropertyChanged += OnChildPropertyChanged;
    }

    public AccountListViewModel AccountList => parent.AccountList;

    public AccountAppearanceViewModel Appearance => parent.Appearance;

    public AccountOfflineUuidViewModel OfflineUuid => parent.OfflineUuid;

    public ICommand RequestRenameAccountCommand => parent.RequestRenameAccountCommand;

    private void OnParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == parent.AccountList)
            OnPropertyChanged(nameof(AccountList));
        else if (sender == parent.Appearance)
            OnPropertyChanged(nameof(Appearance));
        else if (sender == parent.OfflineUuid)
            OnPropertyChanged(nameof(OfflineUuid));
    }
}
