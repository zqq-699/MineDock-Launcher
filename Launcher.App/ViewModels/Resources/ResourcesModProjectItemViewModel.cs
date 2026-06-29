using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesModProjectItemViewModel
{
    private static readonly string[] LoaderDisplayOrder =
    [
        "fabric",
        "forge",
        "neoforge",
        "quilt"
    ];

    private readonly IReadOnlyList<string>? minecraftReleaseVersionOrder;
    private readonly string fallbackIconKey;

    public ResourcesModProjectItemViewModel(
        ResourceProject project,
        IReadOnlyList<string>? minecraftReleaseVersionOrder = null,
        string fallbackIconKey = "instance_setting_page/mod")
    {
        Project = project;
        this.minecraftReleaseVersionOrder = minecraftReleaseVersionOrder;
        this.fallbackIconKey = fallbackIconKey;
    }

    public ResourceProject Project { get; }

    public string Title => Project.Title;

    public string Description => Project.Description;

    public string Subtitle => Project.Kind is ResourceProjectKind.Mod
        ? string.Join("  ", SupportedMinecraftVersionsText, SupportedLoadersText, SourceText)
        : string.Join("  ", SupportedMinecraftVersionsText, SourceText);

    public string TrailingText => string.Format(Strings.Resources_ModDownloadsFormat, DownloadsText);

    public string SupportedMinecraftVersionsText => ResourceMinecraftVersionSupportFormatter.Format(
        Project.SupportedMinecraftVersions,
        minecraftReleaseVersionOrder);

    public string SupportedLoadersText => FormatLoaders(Project.SupportedLoaders);

    public string SourceText => Project.Source switch
    {
        ResourceProjectSource.Modrinth => Strings.Resources_ModSourceModrinth,
        ResourceProjectSource.CurseForge => Strings.Resources_ModSourceCurseForge,
        _ => string.Empty
    };

    public string DownloadsText => FormatDownloads(Project.Downloads);

    public string? IconSource => Project.IconUrl;

    public bool ShowsLoaders => Project.Kind is ResourceProjectKind.Mod;

    public string IconKey => string.IsNullOrWhiteSpace(Project.IconUrl)
        ? fallbackIconKey
        : string.Empty;

    private static string FormatLoaders(IReadOnlyList<string> loaders)
    {
        var normalizedLoaders = loaders
            .Where(loader => !string.IsNullOrWhiteSpace(loader))
            .Select(loader => loader.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(loader =>
            {
                var index = Array.IndexOf(LoaderDisplayOrder, loader);
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(loader => loader, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalizedLoaders.Count == 0
            ? Strings.Resources_ModLoadersUnknown
            : string.Join("/", normalizedLoaders);
    }

    private static string FormatDownloads(long downloads)
    {
        if (downloads >= 100_000_000)
            return string.Format(Strings.Resources_ModDownloadsHundredMillionFormat, downloads / 100_000_000d);

        if (downloads >= 10_000)
            return string.Format(Strings.Resources_ModDownloadsTenThousandFormat, downloads / 10_000d);

        return downloads.ToString("N0");
    }
}
