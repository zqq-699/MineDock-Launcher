using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ShaderPackManagementItemViewModel : ObservableObject
{
    public ShaderPackManagementItemViewModel(LocalShaderPack shaderPack)
    {
        SyncFrom(shaderPack);
    }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "instance_setting_page/shader"
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

    public void SyncFrom(LocalShaderPack shaderPack)
    {
        Title = shaderPack.Name;
        Subtitle = string.Equals(shaderPack.Name, shaderPack.FileName, StringComparison.OrdinalIgnoreCase)
            ? null
            : shaderPack.FileName;
        FullPath = shaderPack.FullPath;
        IconSource = shaderPack.IconSource;
        CreatedAt = shaderPack.CreatedAt;
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
