using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed class DownloadInstanceOptionsViewModel : ObservableObject
{
    private readonly DownloadPageViewModel parent;

    public DownloadInstanceOptionsViewModel(DownloadPageViewModel parent)
    {
        this.parent = parent;
        parent.PropertyChanged += OnParentPropertyChanged;
    }

    public string InstanceName
    {
        get => parent.InstanceName;
        set => parent.InstanceName = value;
    }

    public string InstanceNameDuplicateMessage => parent.InstanceNameDuplicateMessage;

    public ObservableCollection<DownloadLoaderOption> LoaderOptions => parent.LoaderOptions;

    public DownloadLoaderOption? SelectedLoaderOption
    {
        get => parent.SelectedLoaderOption;
        set => parent.SelectedLoaderOption = value;
    }

    public ObservableCollection<LoaderVersionInfo> LoaderVersions => parent.LoaderVersions;

    public LoaderVersionInfo? SelectedLoaderVersion
    {
        get => parent.SelectedLoaderVersion;
        set => parent.SelectedLoaderVersion = value;
    }

    public bool ShouldShowLoaderVersionSelector => parent.ShouldShowLoaderVersionSelector;

    public bool HasLoaderVersions => parent.HasLoaderVersions;

    public string LoaderVersionPlaceholderText => parent.LoaderVersionPlaceholderText;

    public ICommand SelectLoaderOptionCommand => parent.SelectLoaderOptionCommand;

    private void OnParentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(e.PropertyName);
    }
}
