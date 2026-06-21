using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ModManagementModItemViewModel : ObservableObject
{
    public ModManagementModItemViewModel(LocalMod mod)
    {
        Title = string.IsNullOrWhiteSpace(mod.Name)
            ? GetDisplayFileNameWithoutModExtensions(mod.FileName)
            : mod.Name;
        Loader = NormalizeSubtitlePart(mod.Loader);
        ModId = NormalizeSubtitlePart(mod.ModId);
        Version = NormalizeSubtitlePart(mod.Version);
        FileName = mod.FileName;
        FullPath = mod.FullPath;
        IconSource = mod.IconSource;
        IsEnabled = mod.IsEnabled;
    }

    public string Title { get; }

    public string FileName { get; }

    public string FullPath { get; }

    public string Subtitle
    {
        get
        {
            var parts = new[] { Loader, ModId, Version }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .ToArray();
            return parts.Length == 0
                ? GetDisplayFileNameWithoutModExtensions(FileName)
                : string.Join("-", parts);
        }
    }

    public string TrailingText => IsEnabled
        ? Strings.GameSettings_ModManagementEnabledState
        : Strings.GameSettings_ModManagementDisabledState;

    public string? IconSource { get; }

    public string? Loader { get; }

    public string? ModId { get; }

    public string? Version { get; }

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "instance_setting_page/mod"
        : string.Empty;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private bool isSelected;

    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(TrailingText));
    }

    private static string? NormalizeSubtitlePart(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string GetDisplayFileNameWithoutModExtensions(string fileName)
    {
        if (fileName.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
            return fileName[..^".jar.disabled".Length];

        if (fileName.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            return fileName[..^".jar".Length];

        return Path.GetFileNameWithoutExtension(fileName);
    }
}
