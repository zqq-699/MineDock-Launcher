using Launcher.App.Resources;
using Launcher.Domain.Models;

namespace Launcher.App.ViewModels.Download;

public sealed class DownloadQuiltLibraryVersionOption
{
    public DownloadQuiltLibraryVersionOption(string title, string? versionId, string versionNumber, bool isInstallable, bool isLatest, bool isStable)
    {
        Title = title;
        VersionId = versionId;
        VersionNumber = versionNumber;
        IsInstallable = isInstallable;
        IsLatest = isLatest;
        IsStable = isStable;
    }

    public string Title { get; }

    public string? VersionId { get; }

    public string VersionNumber { get; }

    public bool IsInstallable { get; }

    public bool IsLatest { get; }

    public bool IsStable { get; }

    public string TagText => !IsInstallable
        ? string.Empty
        : IsLatest
            ? Strings.Download_QuiltLibraryLatestTag
            : IsStable
                ? Strings.Download_LoaderVersionStableTag
                : Strings.Download_LoaderVersionPreviewTag;

    public static DownloadQuiltLibraryVersionOption None { get; } = new(
        Strings.Download_QuiltLibraryNone,
        null,
        string.Empty,
        isInstallable: false,
        isLatest: false,
        isStable: true);

    public static DownloadQuiltLibraryVersionOption FromVersion(ModrinthVersionInfo version, bool isLatest)
    {
        var title = string.IsNullOrWhiteSpace(version.VersionNumber)
            ? version.Name
            : version.VersionNumber;
        return new DownloadQuiltLibraryVersionOption(
            title,
            version.VersionId,
            version.VersionNumber,
            isInstallable: true,
            isLatest,
            version.IsStable);
    }

    public override string ToString() => Title;
}
