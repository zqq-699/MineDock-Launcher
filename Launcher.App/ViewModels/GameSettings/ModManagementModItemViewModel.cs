using CommunityToolkit.Mvvm.ComponentModel;
using Launcher.App.Resources;
using Launcher.Domain.Models;
using System.IO;

namespace Launcher.App.ViewModels.GameSettings;

public sealed partial class ModManagementModItemViewModel : ObservableObject
{
    public ModManagementModItemViewModel(LocalMod mod)
    {
        SyncFrom(mod);
    }

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

    public string IconKey => string.IsNullOrWhiteSpace(IconSource)
        ? "instance_setting_page/mod"
        : string.Empty;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private string fullPath = string.Empty;

    [ObservableProperty]
    private string? iconSource;

    [ObservableProperty]
    private string? loader;

    [ObservableProperty]
    private string? modId;

    [ObservableProperty]
    private string? version;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private bool isSelected;

    public void SyncFrom(LocalMod mod)
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

    partial void OnFileNameChanged(string value)
    {
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnIconSourceChanged(string? value)
    {
        OnPropertyChanged(nameof(IconKey));
    }

    partial void OnLoaderChanged(string? value)
    {
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnModIdChanged(string? value)
    {
        OnPropertyChanged(nameof(Subtitle));
    }

    partial void OnVersionChanged(string? value)
    {
        OnPropertyChanged(nameof(Subtitle));
    }

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
