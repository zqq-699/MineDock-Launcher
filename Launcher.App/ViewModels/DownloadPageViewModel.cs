using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Launcher.Core.Models;
using Launcher.Core.Services;

namespace Launcher.App.ViewModels;

public sealed partial class DownloadPageViewModel : ObservableObject
{
    private readonly IGameVersionService gameVersionService;
    private bool hasLoadedVersions;
    private DownloadMinecraftVersionItem? hoveredMinecraftVersion;

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

    public DownloadPageViewModel(IGameVersionService gameVersionService)
    {
        this.gameVersionService = gameVersionService;

        VersionCategories.Add(new DownloadVersionCategory("release", "\u6b63\u5f0f\u7248", string.Empty, "instance_download_page/release"));
        VersionCategories.Add(new DownloadVersionCategory("snapshot", "\u5feb\u7167\u7248", string.Empty, "instance_download_page/snapshot"));
        VersionCategories.Add(new DownloadVersionCategory("old_beta", "beta", "\u03b2"));
        VersionCategories.Add(new DownloadVersionCategory("old_alpha", "alpha", "\u03b1"));

        SelectVersionCategory(VersionCategories.First());
    }

    public ObservableCollection<DownloadVersionCategory> VersionCategories { get; } = [];

    public ObservableCollection<DownloadMinecraftVersionItem> AllVersions { get; } = [];

    public ObservableCollection<DownloadMinecraftVersionItem> VisibleVersions { get; } = [];

    public bool HasVisibleVersions => VisibleVersions.Count > 0;

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
            foreach (var version in versions)
                AllVersions.Add(new DownloadMinecraftVersionItem(version));

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
        SelectedVersionCategory = category;
        foreach (var item in VersionCategories)
            item.IsSelected = ReferenceEquals(item, category);

        RefreshVisibleVersions();
    }

    [RelayCommand]
    private void SelectMinecraftVersion(DownloadMinecraftVersionItem version)
    {
        SelectedMinecraftVersion = version;
        foreach (var item in AllVersions)
            item.IsSelected = ReferenceEquals(item, version);

        UpdateVisibleVersionState();
    }

    public void SetHoveredMinecraftVersion(DownloadMinecraftVersionItem? version)
    {
        if (ReferenceEquals(hoveredMinecraftVersion, version))
            return;

        hoveredMinecraftVersion = version is not null && VisibleVersions.Contains(version)
            ? version
            : null;
        UpdateVisibleVersionState();
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
        RefreshVisibleVersions();
    }

    private void RefreshVisibleVersions()
    {
        VisibleVersions.Clear();
        VersionEmptyMessage = string.Empty;

        if (HasVersionLoadError)
        {
            UpdateVisibleVersionState();
            return;
        }

        if (SelectedVersionCategory?.Id is not "release")
        {
            VersionEmptyMessage = "\u8be5\u5206\u7c7b\u7a0d\u540e\u5b9e\u73b0\u3002";
            ClearSelectedVersion();
            UpdateVisibleVersionState();
            return;
        }

        var query = VersionSearchQuery.Trim();
        var versions = AllVersions.Where(version => version.IsRelease);
        if (!string.IsNullOrWhiteSpace(query))
            versions = versions.Where(version => version.Name.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var version in versions)
            VisibleVersions.Add(version);

        if (VisibleVersions.Count == 0 && hasLoadedVersions && !IsLoadingVersions)
            VersionEmptyMessage = string.IsNullOrWhiteSpace(query)
                ? "\u6ca1\u6709\u627e\u5230\u6b63\u5f0f\u7248\u7248\u672c\u3002"
                : "\u6ca1\u6709\u627e\u5230\u5339\u914d\u7684\u7248\u672c\u3002";

        if (SelectedMinecraftVersion is not null && !VisibleVersions.Contains(SelectedMinecraftVersion))
            ClearSelectedVersion();

        UpdateVisibleVersionState();
    }

    private void ClearSelectedVersion()
    {
        SelectedMinecraftVersion = null;
        foreach (var item in AllVersions)
            item.IsSelected = false;
    }

    private void UpdateVisibleVersionState()
    {
        for (var i = 0; i < VisibleVersions.Count; i++)
        {
            var item = VisibleVersions[i];
            item.IsFirstVisible = i == 0;
            item.IsLastVisible = i == VisibleVersions.Count - 1;
            item.IsHovered = ReferenceEquals(item, hoveredMinecraftVersion);
            item.IsPreviousItemHighlighted = i > 0 && IsHighlighted(VisibleVersions[i - 1]);
            item.IsNextItemHighlighted = i < VisibleVersions.Count - 1 && IsHighlighted(VisibleVersions[i + 1]);
        }

        OnPropertyChanged(nameof(HasVisibleVersions));
        OnPropertyChanged(nameof(HasVersionEmptyMessage));
    }

    private bool IsHighlighted(DownloadMinecraftVersionItem item)
    {
        return item.IsSelected || ReferenceEquals(item, hoveredMinecraftVersion);
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

    public string TypeLabel => IsRelease ? "\u6b63\u5f0f\u7248" : Version.Type;

    public bool IsRelease => Version.Type.Equals("Release", StringComparison.OrdinalIgnoreCase);

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isFirstVisible;

    [ObservableProperty]
    private bool isLastVisible;

    [ObservableProperty]
    private bool isHovered;

    [ObservableProperty]
    private bool isPreviousItemHighlighted;

    [ObservableProperty]
    private bool isNextItemHighlighted;
}
