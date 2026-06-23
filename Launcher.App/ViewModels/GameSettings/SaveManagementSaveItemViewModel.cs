using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class SaveManagementSaveItemViewModel : ObservableObject
{
    public SaveManagementSaveItemViewModel(LocalSave save)
    {
        SyncFrom(save);
    }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "instance_setting_page/saves"
        : string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string? subtitle;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string? iconSource;

    [ObservableProperty]
    private DateTimeOffset createdAt;

    [ObservableProperty]
    private bool isSelected;

    public void SyncFrom(LocalSave save)
    {
        Title = string.IsNullOrWhiteSpace(save.Name)
            ? save.DirectoryName
            : save.Name;
        Subtitle = string.Equals(Title, save.DirectoryName, StringComparison.OrdinalIgnoreCase)
            ? null
            : save.DirectoryName;
        FullPath = save.FullPath;
        IconSource = save.IconSource;
        CreatedAt = save.CreatedAt;
    }

    partial void OnIconSourceChanged(string? value)
    {
        OnPropertyChanged(nameof(IconKey));
    }

    partial void OnCreatedAtChanged(DateTimeOffset value)
    {
        OnPropertyChanged(nameof(TrailingText));
    }
}
