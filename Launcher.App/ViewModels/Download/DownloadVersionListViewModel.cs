using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Launcher.App.ViewModels.Download;

public sealed class DownloadVersionListViewModel : ObservableObject
{
    private readonly DownloadPageViewModel parent;

    public DownloadVersionListViewModel(DownloadPageViewModel parent)
    {
        this.parent = parent;
        parent.PropertyChanged += OnParentPropertyChanged;
    }

    public IReadOnlyList<DownloadMinecraftVersionItem> VisibleVersions => parent.VisibleVersions;

    public DownloadMinecraftVersionItem? SelectedMinecraftVersion => parent.SelectedMinecraftVersion;

    public int ListEntranceAnimationToken => parent.ListEntranceAnimationToken;

    public ICommand SelectMinecraftVersionCommand => parent.SelectMinecraftVersionCommand;

    private void OnParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
