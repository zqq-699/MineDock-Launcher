using Launcher.Core.Models;

namespace Launcher.App.Models;

internal static class AccountMapper
{
    public static LauncherAccount FromOfflineRecord(LauncherAccountRecord record)
    {
        return new LauncherAccount
        {
            Id = record.Id,
            DisplayName = record.DisplayName,
            Uuid = record.Uuid,
            AvatarSource = record.AvatarSource,
            IsOffline = true,
            CachedCapeOptions = ToCapeOptions(record.Capes)
        };
    }

    public static LauncherAccount MergeStoredRecord(LauncherAccount account, LauncherAccountRecord record)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = string.IsNullOrWhiteSpace(record.DisplayName) ? account.DisplayName : record.DisplayName,
            Uuid = account.Uuid,
            AvatarSource = string.IsNullOrWhiteSpace(account.AvatarSource) ? record.AvatarSource : account.AvatarSource,
            IsOffline = account.IsOffline,
            CachedCapeOptions = ToCapeOptions(record.Capes)
        };
    }

    public static LauncherAccount WithCapeCache(
        LauncherAccount account,
        IReadOnlyList<AccountCapeOption> capeOptions)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            Uuid = account.Uuid,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            CachedCapeOptions = capeOptions
        };
    }

    public static LauncherAccount WithDisplayName(LauncherAccount account, string displayName)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = displayName,
            Uuid = account.Uuid,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            CachedCapeOptions = account.CachedCapeOptions
        };
    }

    public static LauncherAccountRecord ToRecord(LauncherAccount account)
    {
        return new LauncherAccountRecord
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            Uuid = account.Uuid,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            Capes = account.CachedCapeOptions.Select(ToCapeRecord).ToList()
        };
    }

    private static List<AccountCapeOption> ToCapeOptions(IEnumerable<LauncherCapeRecord>? records)
    {
        if (records is null)
            return [];

        return records
            .Where(record => record.IsNone || !string.IsNullOrWhiteSpace(record.DisplayName))
            .Select(record => new AccountCapeOption
            {
                Id = record.Id,
                DisplayName = string.IsNullOrWhiteSpace(record.DisplayName)
                    ? "\u4e0d\u4f7f\u7528\u62ab\u98ce"
                    : record.DisplayName,
                ImageUrl = record.ImageUrl,
                IsActive = record.IsActive,
                IsNone = record.IsNone
            })
            .ToList();
    }

    private static LauncherCapeRecord ToCapeRecord(AccountCapeOption cape)
    {
        return new LauncherCapeRecord
        {
            Id = cape.Id,
            DisplayName = cape.DisplayName,
            ImageUrl = cape.ImageUrl,
            IsActive = cape.IsActive,
            IsNone = cape.IsNone
        };
    }
}
