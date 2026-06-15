using Launcher.Domain.Models;

namespace Launcher.Application.Accounts;

public static class AccountMapper
{
    public static LauncherAccount FromRecord(LauncherAccountRecord record)
    {
        return new LauncherAccount
        {
            Id = record.Id,
            DisplayName = record.DisplayName,
            Uuid = record.Uuid,
            OfflineUuidGenerationMode = record.OfflineUuidGenerationMode,
            AvatarSource = record.AvatarSource,
            IsOffline = record.IsOffline,
            CachedCapeOptions = ToCapeOptions(record.Capes)
        };
    }

    public static LauncherAccount FromOfflineRecord(LauncherAccountRecord record)
    {
        return FromRecord(record);
    }

    public static LauncherAccount MergeStoredRecord(LauncherAccount account, LauncherAccountRecord record)
    {
        var storedCapeOptions = ToCapeOptions(record.Capes);
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = account.HasFreshProfile && !string.IsNullOrWhiteSpace(account.DisplayName)
                ? account.DisplayName
                : string.IsNullOrWhiteSpace(record.DisplayName) ? account.DisplayName : record.DisplayName,
            Uuid = account.Uuid,
            OfflineUuidGenerationMode = record.OfflineUuidGenerationMode,
            AvatarSource = string.IsNullOrWhiteSpace(account.AvatarSource) ? record.AvatarSource : account.AvatarSource,
            IsOffline = account.IsOffline,
            HasFreshProfile = account.HasFreshProfile,
            CachedCapeOptions = account.HasFreshProfile && account.CachedCapeOptions.Count > 0
                ? account.CachedCapeOptions
                : storedCapeOptions
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
            OfflineUuidGenerationMode = account.OfflineUuidGenerationMode,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            HasFreshProfile = account.HasFreshProfile,
            CachedCapeOptions = capeOptions
        };
    }

    public static LauncherAccount WithAvatarFallback(LauncherAccount account, string? avatarSource)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            Uuid = account.Uuid,
            OfflineUuidGenerationMode = account.OfflineUuidGenerationMode,
            AvatarSource = string.IsNullOrWhiteSpace(account.AvatarSource) ? avatarSource : account.AvatarSource,
            IsOffline = account.IsOffline,
            HasFreshProfile = account.HasFreshProfile,
            CachedCapeOptions = account.CachedCapeOptions
        };
    }

    public static LauncherAccount WithDisplayName(LauncherAccount account, string displayName)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = displayName,
            Uuid = account.Uuid,
            OfflineUuidGenerationMode = account.OfflineUuidGenerationMode,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            HasFreshProfile = account.HasFreshProfile,
            CachedCapeOptions = account.CachedCapeOptions
        };
    }

    public static LauncherAccount WithOfflineUuid(
        LauncherAccount account,
        OfflineUuidGenerationMode mode,
        string uuid)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            Uuid = uuid,
            OfflineUuidGenerationMode = mode,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            HasFreshProfile = account.HasFreshProfile,
            CachedCapeOptions = account.CachedCapeOptions
        };
    }

    public static LauncherAccount WithDisplayNameAndOfflineUuid(
        LauncherAccount account,
        string displayName,
        OfflineUuidGenerationMode mode,
        string uuid)
    {
        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = displayName,
            Uuid = uuid,
            OfflineUuidGenerationMode = mode,
            AvatarSource = account.AvatarSource,
            IsOffline = account.IsOffline,
            HasFreshProfile = account.HasFreshProfile,
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
            OfflineUuidGenerationMode = account.OfflineUuidGenerationMode,
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
