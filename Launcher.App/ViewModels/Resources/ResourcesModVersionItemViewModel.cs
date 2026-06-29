using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesModVersionItemViewModel
{
    public ResourcesModVersionItemViewModel(
        ResourceProjectVersion version,
        ResourcesModProjectItemViewModel? project,
        string fallbackIconKey = "instance_setting_page/mod")
    {
        Version = version;
        IconSource = project?.IconSource;
        IconKey = string.IsNullOrWhiteSpace(IconSource)
            ? fallbackIconKey
            : string.Empty;
    }

    public ResourceProjectVersion Version { get; }

    public string? IconSource { get; }

    public string IconKey { get; }

    public string Title => string.IsNullOrWhiteSpace(Version.Name)
        ? Version.VersionNumber
        : Version.Name;

    public string Subtitle => string.IsNullOrWhiteSpace(Version.FileName)
        ? FormatSubtitle(Version.VersionNumber)
        : FormatSubtitle(Version.FileName);

    public string TrailingText => Version.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? string.Empty;

    private string VersionTypeText => Version.VersionType.Trim().ToLowerInvariant() switch
    {
        "release" => Strings.Download_ReleaseCategory,
        "beta" => Strings.Download_BetaCategory,
        "alpha" => Strings.Download_AlphaCategory,
        _ => string.Empty
    };

    private string FormatSubtitle(string value)
    {
        return string.IsNullOrWhiteSpace(VersionTypeText)
            ? value
            : $"{value}  {VersionTypeText}";
    }
}
