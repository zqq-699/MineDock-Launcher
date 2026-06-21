using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class SaveManagementSaveItemViewModel : ObservableObject
{
    public SaveManagementSaveItemViewModel(LocalSave save)
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

    public string Title { get; }

    public string? Subtitle { get; }

    public string FullPath { get; }

    public string? IconSource { get; }

    public DateTimeOffset CreatedAt { get; }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "instance_setting_page/saves"
        : string.Empty;

    [ObservableProperty]
    private bool isSelected;
}
