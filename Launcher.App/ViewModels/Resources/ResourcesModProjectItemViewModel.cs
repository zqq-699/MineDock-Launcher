using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Resources;

public sealed class ResourcesModProjectItemViewModel
{
    public ResourcesModProjectItemViewModel(ResourceProject project)
    {
        Project = project;
    }

    public ResourceProject Project { get; }

    public string Title => Project.Title;

    public string Subtitle => string.IsNullOrWhiteSpace(Project.Description)
        ? SourceText
        : $"{SourceText} - {Project.Description}";

    public string TrailingText => string.Format(Strings.Resources_ModDownloadsFormat, FormatDownloads(Project.Downloads));

    public string? IconSource => Project.IconUrl;

    public string IconKey => string.IsNullOrWhiteSpace(Project.IconUrl)
        ? "instance_setting_page/mod"
        : string.Empty;

    private string SourceText => Project.Source switch
    {
        ResourceProjectSource.Modrinth => Strings.Resources_ModSourceModrinth,
        ResourceProjectSource.CurseForge => Strings.Resources_ModSourceCurseForge,
        _ => string.Empty
    };

    private static string FormatDownloads(long downloads)
    {
        if (downloads >= 100_000_000)
            return string.Format(Strings.Resources_ModDownloadsHundredMillionFormat, downloads / 100_000_000d);

        if (downloads >= 10_000)
            return string.Format(Strings.Resources_ModDownloadsTenThousandFormat, downloads / 10_000d);

        return downloads.ToString("N0");
    }
}
