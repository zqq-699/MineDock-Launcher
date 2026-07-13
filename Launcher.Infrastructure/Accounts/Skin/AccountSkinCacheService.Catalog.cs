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

using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

internal sealed partial class AccountSkinCacheService
{
public IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account)
    {
        // 合并账户元数据与磁盘遗留文件，让升级前缓存仍可见，同时按内容去重。
        var uuid = account.Uuid ?? account.Id;
        if (string.IsNullOrWhiteSpace(uuid))
            return [];

        foreach (var skin in account.SkinLibrary)
            TryCopyExistingSkinIntoAccountDirectory(uuid, skin);

        var accountSkinDirectory = GetAccountSkinDirectory(uuid);
        if (!Directory.Exists(accountSkinDirectory))
            return [];

        return Directory.EnumerateFiles(accountSkinDirectory, "*.png")
            .Select(path => TryCreateRecordForFile(account.SkinLibrary, path))
            .Where(record => record is not null)
            .Select(record => record!)
            .OrderBy(record => record.AddedAtUtc)
            .ToList();
    }

    private void TryCopyExistingSkinIntoAccountDirectory(string uuid, LauncherSkinRecord skin)
    {
        // 历史版本可能把皮肤放在共享目录；复制到新目录而非移动，以保证迁移失败可回退。
        var sourcePath = ResolveSkinSourcePath(skin.Source);
        if (sourcePath is null || !File.Exists(sourcePath))
            return;

        try
        {
            var hash = ComputeSkinContentHash(File.ReadAllBytes(sourcePath));
            var targetPath = CreateSkinPath(uuid, hash);
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                return;

            if (!File.Exists(targetPath))
                File.Copy(sourcePath, targetPath);
        }
        catch
        {
        }
    }

    private LauncherSkinRecord? TryCreateRecordForFile(IReadOnlyList<LauncherSkinRecord> skins, string skinPath)
    {
        try
        {
            var hash = ComputeSkinContentHash(File.ReadAllBytes(skinPath));
            var skinModel = TryParseSkinModel(skinPath) ?? FindModelForFile(skins, skinPath, hash) ?? MinecraftSkinModel.Classic;
            var existing = FindExisting(skins, hash, skinModel)
                ?? FindExistingBySource(skins, skinPath);
            return existing is null
                ? CreateRecord(hash, skinModel, new Uri(skinPath).AbsoluteUri)
                : CopyRecordWithSource(existing, new Uri(skinPath).AbsoluteUri, hash);
        }
        catch
        {
            return null;
        }
    }
}
