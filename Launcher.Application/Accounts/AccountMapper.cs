/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, version 3.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

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
            SkinSource = record.SkinSource,
            SkinModel = record.SkinModel,
            SkinLibrary = CopySkinRecords(record.Skins),
            ActiveSkinId = record.ActiveSkinId,
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
        var mergedSkinLibrary = MergeSkinLibrary(account.SkinLibrary, record.Skins);
        var activeSkinId = ResolveActiveSkinId(account, record, mergedSkinLibrary);
        return CreateCopy(
            account,
            displayName: account.HasFreshProfile && !string.IsNullOrWhiteSpace(account.DisplayName)
                ? account.DisplayName
                : string.IsNullOrWhiteSpace(record.DisplayName) ? account.DisplayName : record.DisplayName,
            offlineUuidGenerationMode: record.OfflineUuidGenerationMode,
            avatarSource: string.IsNullOrWhiteSpace(account.AvatarSource) ? record.AvatarSource : account.AvatarSource,
            skinSource: string.IsNullOrWhiteSpace(account.SkinSource) ? record.SkinSource : account.SkinSource,
            skinModel: account.SkinModel ?? record.SkinModel,
            skinLibrary: mergedSkinLibrary,
            activeSkinId: activeSkinId,
            cachedCapeOptions: account.HasFreshProfile && account.CachedCapeOptions.Count > 0
                ? account.CachedCapeOptions
                : storedCapeOptions);
    }

    public static LauncherAccount WithCapeCache(
        LauncherAccount account,
        IReadOnlyList<AccountCapeOption> capeOptions)
    {
        return CreateCopy(account, cachedCapeOptions: capeOptions);
    }

    public static LauncherAccount WithAvatarFallback(LauncherAccount account, string? avatarSource)
    {
        return CreateCopy(
            account,
            avatarSource: string.IsNullOrWhiteSpace(account.AvatarSource) ? avatarSource : account.AvatarSource);
    }

    public static LauncherAccount WithAppearanceFallback(LauncherAccount account, LauncherAccount fallback)
    {
        return CreateCopy(
            account,
            avatarSource: string.IsNullOrWhiteSpace(account.AvatarSource) ? fallback.AvatarSource : account.AvatarSource,
            skinSource: string.IsNullOrWhiteSpace(account.SkinSource) ? fallback.SkinSource : account.SkinSource,
            skinModel: account.SkinModel ?? fallback.SkinModel,
            skinLibrary: account.SkinLibrary.Count > 0 ? account.SkinLibrary : fallback.SkinLibrary,
            activeSkinId: string.IsNullOrWhiteSpace(account.ActiveSkinId) ? fallback.ActiveSkinId : account.ActiveSkinId);
    }

    public static LauncherAccount WithSkinLibrary(
        LauncherAccount account,
        IReadOnlyList<LauncherSkinRecord> skinLibrary,
        string? activeSkinId,
        string? skinSource,
        MinecraftSkinModel? skinModel)
    {
        return CreateCopy(
            account,
            skinSource: skinSource,
            skinModel: skinModel,
            skinLibrary: skinLibrary,
            activeSkinId: activeSkinId);
    }

    public static LauncherAccount WithDisplayName(LauncherAccount account, string displayName)
    {
        return CreateCopy(account, displayName: displayName);
    }

    public static LauncherAccount WithOfflineUuid(
        LauncherAccount account,
        OfflineUuidGenerationMode mode,
        string uuid)
    {
        return CreateCopy(account, uuid: uuid, offlineUuidGenerationMode: mode);
    }

    public static LauncherAccount WithDisplayNameAndOfflineUuid(
        LauncherAccount account,
        string displayName,
        OfflineUuidGenerationMode mode,
        string uuid)
    {
        return CreateCopy(
            account,
            displayName: displayName,
            uuid: uuid,
            offlineUuidGenerationMode: mode);
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
            SkinSource = account.SkinSource,
            SkinModel = account.SkinModel,
            Skins = CopySkinRecords(account.SkinLibrary),
            ActiveSkinId = account.ActiveSkinId,
            IsOffline = account.IsOffline,
            Capes = account.CachedCapeOptions.Select(ToCapeRecord).ToList()
        };
    }

    private static LauncherAccount CreateCopy(
        LauncherAccount account,
        string? id = null,
        string? displayName = null,
        string? uuid = null,
        OfflineUuidGenerationMode? offlineUuidGenerationMode = null,
        string? avatarSource = null,
        string? skinSource = null,
        MinecraftSkinModel? skinModel = null,
        IReadOnlyList<LauncherSkinRecord>? skinLibrary = null,
        string? activeSkinId = null,
        bool? isOffline = null,
        bool? hasFreshProfile = null,
        IReadOnlyList<AccountCapeOption>? cachedCapeOptions = null)
    {
        return new LauncherAccount
        {
            Id = id ?? account.Id,
            DisplayName = displayName ?? account.DisplayName,
            Uuid = uuid ?? account.Uuid,
            OfflineUuidGenerationMode = offlineUuidGenerationMode ?? account.OfflineUuidGenerationMode,
            AvatarSource = avatarSource ?? account.AvatarSource,
            SkinSource = skinSource ?? account.SkinSource,
            SkinModel = skinModel ?? account.SkinModel,
            SkinLibrary = CopySkinRecords(skinLibrary ?? account.SkinLibrary),
            ActiveSkinId = activeSkinId ?? account.ActiveSkinId,
            IsOffline = isOffline ?? account.IsOffline,
            HasFreshProfile = hasFreshProfile ?? account.HasFreshProfile,
            CachedCapeOptions = cachedCapeOptions ?? account.CachedCapeOptions
        };
    }

    private static List<LauncherSkinRecord> MergeSkinLibrary(
        IReadOnlyList<LauncherSkinRecord>? refreshedSkins,
        IReadOnlyList<LauncherSkinRecord>? storedSkins)
    {
        var skins = CopySkinRecords(storedSkins);
        foreach (var skin in refreshedSkins ?? [])
        {
            if (skins.Any(existing => string.Equals(existing.Id, skin.Id, StringComparison.Ordinal)
                    || (!string.IsNullOrWhiteSpace(existing.ContentHash)
                        && string.Equals(existing.ContentHash, skin.ContentHash, StringComparison.OrdinalIgnoreCase)
                        && existing.SkinModel == skin.SkinModel)))
            {
                continue;
            }

            skins.Add(CopySkinRecord(skin));
        }

        return skins;
    }

    private static string? ResolveActiveSkinId(
        LauncherAccount refreshedAccount,
        LauncherAccountRecord storedRecord,
        IReadOnlyList<LauncherSkinRecord> mergedSkinLibrary)
    {
        if (!string.IsNullOrWhiteSpace(refreshedAccount.ActiveSkinId))
        {
            var refreshedSkin = refreshedAccount.SkinLibrary.FirstOrDefault(skin =>
                string.Equals(skin.Id, refreshedAccount.ActiveSkinId, StringComparison.Ordinal));
            var matchingSkin = FindMatchingSkin(mergedSkinLibrary, refreshedSkin);
            if (matchingSkin is not null)
                return matchingSkin.Id;

            if (mergedSkinLibrary.Any(skin => string.Equals(skin.Id, refreshedAccount.ActiveSkinId, StringComparison.Ordinal)))
                return refreshedAccount.ActiveSkinId;
        }

        if (!string.IsNullOrWhiteSpace(storedRecord.ActiveSkinId)
            && mergedSkinLibrary.Any(skin => string.Equals(skin.Id, storedRecord.ActiveSkinId, StringComparison.Ordinal)))
        {
            return storedRecord.ActiveSkinId;
        }

        return null;
    }

    private static LauncherSkinRecord? FindMatchingSkin(
        IReadOnlyList<LauncherSkinRecord> skins,
        LauncherSkinRecord? target)
    {
        if (target is null)
            return null;

        return skins.FirstOrDefault(skin =>
            skin.SkinModel == target.SkinModel
            && string.Equals(skin.ContentHash, target.ContentHash, StringComparison.OrdinalIgnoreCase));
    }

    private static List<LauncherSkinRecord> CopySkinRecords(IEnumerable<LauncherSkinRecord>? skins)
    {
        return skins?.Select(CopySkinRecord).ToList() ?? [];
    }

    private static LauncherSkinRecord CopySkinRecord(LauncherSkinRecord skin)
    {
        return new LauncherSkinRecord
        {
            Id = skin.Id,
            Source = skin.Source,
            SkinModel = skin.SkinModel,
            ContentHash = skin.ContentHash,
            AddedAtUtc = skin.AddedAtUtc
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
                DisplayName = record.DisplayName,
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
