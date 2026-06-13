using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

public sealed partial class DownloadPageViewModel : ObservableObject
{
    private readonly IGameVersionService gameVersionService;
    private bool hasLoadedVersions;
    private int refreshRequestVersion;

    [ObservableProperty]
    private DownloadVersionCategory? selectedVersionCategory;

    [ObservableProperty]
    private DownloadMinecraftVersionItem? selectedMinecraftVersion;

    [ObservableProperty]
    private bool isLoadingVersions;

    [ObservableProperty]
    private string versionLoadError = string.Empty;

    [ObservableProperty]
    private string versionEmptyMessage = string.Empty;

    [ObservableProperty]
    private string versionSearchQuery = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<DownloadMinecraftVersionItem> visibleVersions = Array.Empty<DownloadMinecraftVersionItem>();

    public DownloadPageViewModel(IGameVersionService gameVersionService)
    {
        this.gameVersionService = gameVersionService;

        VersionCategories.Add(new DownloadVersionCategory("release", "\u6b63\u5f0f\u7248", string.Empty, "instance_download_page/release"));
        VersionCategories.Add(new DownloadVersionCategory("snapshot", "\u5feb\u7167\u7248", string.Empty, "instance_download_page/snapshot"));
        VersionCategories.Add(new DownloadVersionCategory("old_beta", "beta", "\u03b2"));
        VersionCategories.Add(new DownloadVersionCategory("old_alpha", "alpha", "\u03b1"));

        SelectVersionCategoryCore(VersionCategories.First(), deferRefresh: false);
    }

    public ObservableCollection<DownloadVersionCategory> VersionCategories { get; } = [];

    public List<DownloadMinecraftVersionItem> AllVersions { get; } = [];

    public bool HasVisibleVersions => VisibleVersions.Count > 0;

    public bool HasSelectedMinecraftVersion => SelectedMinecraftVersion is not null;

    public bool HasVersionLoadError => !string.IsNullOrWhiteSpace(VersionLoadError);

    public bool HasVersionEmptyMessage => !string.IsNullOrWhiteSpace(VersionEmptyMessage);

    public async Task EnsureVersionsLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (hasLoadedVersions || IsLoadingVersions)
            return;

        IsLoadingVersions = true;
        VersionLoadError = string.Empty;
        VersionEmptyMessage = string.Empty;

        try
        {
            var versions = await gameVersionService.GetVersionsAsync(cancellationToken);
            AllVersions.Clear();
            AllVersions.AddRange(versions.Select(version => new DownloadMinecraftVersionItem(version)));

            hasLoadedVersions = true;
        }
        catch (Exception ex)
        {
            VersionLoadError = $"\u7248\u672c\u5217\u8868\u52a0\u8f7d\u5931\u8d25\uff1a{ex.Message}";
        }
        finally
        {
            IsLoadingVersions = false;
            RefreshVisibleVersions();
        }
    }

    [RelayCommand]
    private void SelectVersionCategory(DownloadVersionCategory category)
    {
        SelectVersionCategoryCore(category, deferRefresh: false);
    }

    private void SelectVersionCategoryCore(DownloadVersionCategory category, bool deferRefresh)
    {
        SelectedVersionCategory = category;
        foreach (var item in VersionCategories)
            item.IsSelected = ReferenceEquals(item, category);

        RequestVisibleVersionsRefresh(deferRefresh);
    }

    [RelayCommand]
    private void SelectMinecraftVersion(DownloadMinecraftVersionItem version)
    {
        SelectedMinecraftVersion = version;
        foreach (var item in AllVersions)
            item.IsSelected = ReferenceEquals(item, version);
    }

    partial void OnVersionLoadErrorChanged(string value)
    {
        OnPropertyChanged(nameof(HasVersionLoadError));
    }

    partial void OnVersionEmptyMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasVersionEmptyMessage));
    }

    partial void OnVersionSearchQueryChanged(string value)
    {
        RequestVisibleVersionsRefresh(defer: false);
    }

    partial void OnSelectedMinecraftVersionChanged(DownloadMinecraftVersionItem? value)
    {
        OnPropertyChanged(nameof(HasSelectedMinecraftVersion));
    }

    partial void OnVisibleVersionsChanged(IReadOnlyList<DownloadMinecraftVersionItem> value)
    {
        OnPropertyChanged(nameof(HasVisibleVersions));
        OnPropertyChanged(nameof(HasVersionEmptyMessage));
    }

    private void RequestVisibleVersionsRefresh(bool defer)
    {
        var requestVersion = ++refreshRequestVersion;
        var dispatcher = Application.Current?.Dispatcher;
        if (defer && dispatcher is not null && dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(
                () =>
                {
                    if (requestVersion == refreshRequestVersion)
                        RefreshVisibleVersions();
                },
                DispatcherPriority.Background);
            return;
        }

        RefreshVisibleVersions();
    }

    private void RefreshVisibleVersions()
    {
        VersionEmptyMessage = string.Empty;
        IReadOnlyList<DownloadMinecraftVersionItem> nextVersions = Array.Empty<DownloadMinecraftVersionItem>();

        if (HasVersionLoadError)
        {
            SetFilteredVersions(nextVersions);
            return;
        }

        if (SelectedVersionCategory?.Id is not ("release" or "snapshot"))
        {
            VersionEmptyMessage = "\u8be5\u5206\u7c7b\u7a0d\u540e\u5b9e\u73b0\u3002";
            ClearSelectedVersion();
            SetFilteredVersions(nextVersions);
            return;
        }

        var query = VersionSearchQuery.Trim();
        var versions = SelectedVersionCategory?.Id switch
        {
            "snapshot" => AllVersions.Where(version => version.IsSnapshot),
            _ => AllVersions.Where(version => version.IsRelease)
        };
        if (!string.IsNullOrWhiteSpace(query))
            versions = versions.Where(version => version.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        nextVersions = SortVersionsForCategory(versions, SelectedVersionCategory?.Id).ToList();

        if (nextVersions.Count == 0 && hasLoadedVersions && !IsLoadingVersions)
        {
            var categoryTitle = SelectedVersionCategory?.Title ?? "\u7248\u672c";
            VersionEmptyMessage = string.IsNullOrWhiteSpace(query)
                ? $"\u6ca1\u6709\u627e\u5230{categoryTitle}\u7248\u672c\u3002"
                : "\u6ca1\u6709\u627e\u5230\u5339\u914d\u7684\u7248\u672c\u3002";
        }

        if (SelectedMinecraftVersion is not null && !nextVersions.Contains(SelectedMinecraftVersion))
            ClearSelectedVersion();

        SetFilteredVersions(nextVersions);
    }

    private void SetFilteredVersions(IReadOnlyList<DownloadMinecraftVersionItem> versions)
    {
        VisibleVersions = versions;
    }

    private static IEnumerable<DownloadMinecraftVersionItem> SortVersionsForCategory(
        IEnumerable<DownloadMinecraftVersionItem> versions,
        string? categoryId)
    {
        if (categoryId == "snapshot")
        {
            return versions
                .OrderByDescending(version => version.Version.ReleaseTime ?? DateTimeOffset.MinValue)
                .ThenByDescending(version => version.Name, StringComparer.OrdinalIgnoreCase);
        }

        return versions;
    }

    private void ClearSelectedVersion()
    {
        SelectedMinecraftVersion = null;
        foreach (var item in AllVersions)
            item.IsSelected = false;
    }

}

public sealed partial class DownloadVersionCategory : ObservableObject
{
    public DownloadVersionCategory(string id, string title, string icon, string? iconKey = null)
    {
        Id = id;
        Title = title;
        Icon = icon;
        IconKey = iconKey;
    }

    public string Id { get; }

    public string Title { get; }

    public string Icon { get; }

    public string? IconKey { get; }

    public string IconMode => string.IsNullOrWhiteSpace(IconKey) ? "Glyph" : "Svg";

    [ObservableProperty]
    private bool isSelected;
}

public sealed partial class DownloadMinecraftVersionItem : ObservableObject
{
    public DownloadMinecraftVersionItem(MinecraftVersionInfo version)
    {
        Version = version;
    }

    public MinecraftVersionInfo Version { get; }

    public string Name => Version.Name;

    public string Type => Version.Type;

    public string TypeLabel => Version.Type.ToLowerInvariant() switch
    {
        "release" => "\u6b63\u5f0f\u7248",
        "snapshot" => "\u5feb\u7167\u7248",
        _ => Version.Type
    };

    public string ReleaseDateText => Version.ReleaseTime is { } releaseTime
        ? releaseTime.ToLocalTime().ToString("yyyy-MM-dd")
        : string.Empty;

    public bool IsRelease => Version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase);

    public bool IsSnapshot => Version.Type.Equals("Snapshot", StringComparison.OrdinalIgnoreCase);

    public string IconSource => IsSnapshot
        ? "/Assets/Icons/block/dirt_block.png"
        : "/Assets/Icons/block/grass_block.png";

    [ObservableProperty]
    private bool isSelected;
}
