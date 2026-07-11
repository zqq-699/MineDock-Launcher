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

/// <summary>
/// 在持久化账户记录与领域账户之间复制和合并数据，集中维护外观缓存兼容规则。
/// </summary>
public static class AccountMapper
{
    // With 方法都创建新对象，调用方可在成功后原子替换账户而不污染失败前状态。
    public static LauncherAccount FromRecord(LauncherAccountRecord record)
    {
        // 旧记录缺失的集合和外观字段在映射边界补为安全默认值。
        return new LauncherAccount
        {
            Id = record.Id,
            DisplayName = record.DisplayName,
            Kind = ResolveKind(record),
            Uuid = record.Uuid,
            AuthenticationServerUrl = record.AuthenticationServerUrl,
            ThirdPartyLoginUsername = record.ThirdPartyLoginUsername,
            OfflineUuidGenerationMode = record.OfflineUuidGenerationMode,
            AvatarSource = record.AvatarSource,
            SkinSource = record.SkinSource,
            SkinModel = record.SkinModel,
            SkinLibrary = CopySkinRecords(record.Skins),
            ActiveSkinId = record.ActiveSkinId,
            CachedCapeOptions = ToCapeOptions(record.Capes)
        };
    }

    public static LauncherAccount FromOfflineRecord(LauncherAccountRecord record)
    {
        return FromRecord(record);
    }

    public static LauncherAccount MergeStoredRecord(LauncherAccount account, LauncherAccountRecord record)
    {
        // 在线资料优先，存储记录只补充本地皮肤库、头像和披风缓存。
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
        // 远端刷新缺少外观时保留旧缓存，避免短暂 API 缺失让头像消失。
        return CreateCopy(
            account,
            avatarSource: string.IsNullOrWhiteSpace(account.AvatarSource) ? fallback.AvatarSource : account.AvatarSource,
            skinSource: string.IsNullOrWhiteSpace(account.SkinSource) ? fallback.SkinSource : account.SkinSource,
            skinModel: account.SkinModel ?? fallback.SkinModel,
            skinLibrary: account.SkinLibrary.Count > 0 ? account.SkinLibrary : fallback.SkinLibrary,
            activeSkinId: string.IsNullOrWhiteSpace(account.ActiveSkinId) ? fallback.ActiveSkinId : account.ActiveSkinId);
    }

    public static LauncherAccount WithRefreshedAppearance(
        LauncherAccount account,
        string? avatarSource,
        string? skinSource,
        MinecraftSkinModel? skinModel,
        IReadOnlyList<LauncherSkinRecord> refreshedSkins,
        string? activeSkinId)
    {
        var mergedSkins = MergeSkinLibrary(refreshedSkins, account.SkinLibrary);
        return CreateCopy(
            account,
            avatarSource: string.IsNullOrWhiteSpace(avatarSource) ? account.AvatarSource : avatarSource,
            skinSource: string.IsNullOrWhiteSpace(skinSource) ? account.SkinSource : skinSource,
            skinModel: skinModel ?? account.SkinModel,
            skinLibrary: mergedSkins,
            activeSkinId: string.IsNullOrWhiteSpace(activeSkinId) ? account.ActiveSkinId : activeSkinId);
    }

    public static LauncherAccount WithThirdPartyProfile(
        LauncherAccount account,
        string displayName,
        string? avatarSource,
        LauncherSkinRecord? activeSkin,
        AccountCapeOption? activeCape)
    {
        var mergedSkins = MergeSkinLibrary(
            activeSkin is null ? [] : [activeSkin],
            account.SkinLibrary);
        var capes = activeCape is null
            ? new List<AccountCapeOption>
            {
                new() { DisplayName = string.Empty, IsActive = true, IsNone = true }
            }
            : new List<AccountCapeOption> { activeCape };

        return new LauncherAccount
        {
            Id = account.Id,
            DisplayName = displayName,
            Kind = account.Kind,
            Uuid = account.Uuid,
            AuthenticationServerUrl = account.AuthenticationServerUrl,
            ThirdPartyLoginUsername = account.ThirdPartyLoginUsername,
            OfflineUuidGenerationMode = account.OfflineUuidGenerationMode,
            AvatarSource = avatarSource,
            SkinSource = activeSkin?.Source,
            SkinModel = activeSkin?.SkinModel,
            SkinLibrary = mergedSkins,
            ActiveSkinId = activeSkin?.Id,
            HasFreshProfile = true,
            CachedCapeOptions = capes
        };
    }

    public static LauncherAccount WithSkinLibrary(
        LauncherAccount account,
        IReadOnlyList<LauncherSkinRecord> skinLibrary,
        string? activeSkinId,
        string? skinSource,
        MinecraftSkinModel? skinModel)
    {
        // 更新皮肤库时同时校正 ActiveSkinId，删除当前皮肤后不能留下悬空引用。
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
        // 写入前深拷贝可变集合，防止保存过程与 UI 后续修改共享引用。
        return new LauncherAccountRecord
        {
            Id = account.Id,
            DisplayName = account.DisplayName,
            Kind = account.Kind,
            Uuid = account.Uuid,
            AuthenticationServerUrl = account.AuthenticationServerUrl,
            ThirdPartyLoginUsername = account.ThirdPartyLoginUsername,
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
        LauncherAccountKind? kind = null,
        string? uuid = null,
        OfflineUuidGenerationMode? offlineUuidGenerationMode = null,
        string? avatarSource = null,
        string? skinSource = null,
        MinecraftSkinModel? skinModel = null,
        IReadOnlyList<LauncherSkinRecord>? skinLibrary = null,
        string? activeSkinId = null,
        bool? hasFreshProfile = null,
        IReadOnlyList<AccountCapeOption>? cachedCapeOptions = null)
    {
        return new LauncherAccount
        {
            Id = id ?? account.Id,
            DisplayName = displayName ?? account.DisplayName,
            Kind = kind ?? account.Kind,
            Uuid = uuid ?? account.Uuid,
            AuthenticationServerUrl = account.AuthenticationServerUrl,
            ThirdPartyLoginUsername = account.ThirdPartyLoginUsername,
            OfflineUuidGenerationMode = offlineUuidGenerationMode ?? account.OfflineUuidGenerationMode,
            AvatarSource = avatarSource ?? account.AvatarSource,
            SkinSource = skinSource ?? account.SkinSource,
            SkinModel = skinModel ?? account.SkinModel,
            SkinLibrary = CopySkinRecords(skinLibrary ?? account.SkinLibrary),
            ActiveSkinId = activeSkinId ?? account.ActiveSkinId,
            HasFreshProfile = hasFreshProfile ?? account.HasFreshProfile,
            CachedCapeOptions = cachedCapeOptions ?? account.CachedCapeOptions
        };
    }

    private static LauncherAccountKind ResolveKind(LauncherAccountRecord record)
    {
        return record.Kind ?? (record.IsOffline
            ? LauncherAccountKind.Offline
            : LauncherAccountKind.Microsoft);
    }

    private static List<LauncherSkinRecord> MergeSkinLibrary(
        IReadOnlyList<LauncherSkinRecord>? refreshedSkins,
        IReadOnlyList<LauncherSkinRecord>? storedSkins)
    {
        // 以稳定 Id 优先、内容身份兜底去重，兼容早期没有皮肤 Id 的记录。
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
        // 活动 Id 必须存在于合并后的库，否则回退当前皮肤或首项。
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
