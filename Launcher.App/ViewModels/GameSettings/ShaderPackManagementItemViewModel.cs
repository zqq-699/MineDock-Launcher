using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ShaderPackManagementItemViewModel : ObservableObject
{
    public ShaderPackManagementItemViewModel(LocalShaderPack shaderPack)
    {
        Title = shaderPack.Name;
        Subtitle = string.Equals(shaderPack.Name, shaderPack.FileName, StringComparison.OrdinalIgnoreCase)
            ? null
            : shaderPack.FileName;
        FullPath = shaderPack.FullPath;
        IconSource = shaderPack.IconSource;
        CreatedAt = shaderPack.CreatedAt;
    }

    public string Title { get; }

    public string? Subtitle { get; }

    public string FullPath { get; }

    public string? IconSource { get; }

    public DateTimeOffset CreatedAt { get; }

    public string TrailingText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "instance_setting_page/shader"
        : string.Empty;

    [ObservableProperty]
    private bool isSelected;
}
