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
public async Task<LauncherSkinRecord?> GetOrCreateSkinRecordFromUrlAsync(
        string uuid,
        string? skinUrl,
        MinecraftSkinModel skinModel,
        IReadOnlyList<LauncherSkinRecord> existingSkins,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        // URL 可能变化但内容相同，使用像素内容哈希匹配已有记录而不是只比较地址。
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(skinUrl))
            return null;

        try
        {
            var skinBytes = await httpClient.GetByteArrayAsync(skinUrl, cancellationToken);
            var hash = ComputeSkinContentHash(skinBytes);
            var skinPath = CreateSkinPath(uuid, hash);
            if (!File.Exists(skinPath) || forceRefresh)
                await File.WriteAllBytesAsync(skinPath, skinBytes, cancellationToken);

            var existing = FindExisting(existingSkins, hash, skinModel);
            return existing is null
                ? CreateRecord(hash, skinModel, new Uri(skinPath).AbsoluteUri)
                : CopyRecordWithSource(existing, new Uri(skinPath).AbsoluteUri, hash);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LauncherSkinRecord> ImportSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken)
    {
        // 导入先完整读取和解码，再写账户目录，源文件始终由用户保留。
        var uuid = account.Uuid ?? account.Id;
        var skinBytes = await File.ReadAllBytesAsync(skinFilePath, cancellationToken);
        var hash = ComputeSkinContentHash(skinBytes);
        var skinPath = CreateSkinPath(uuid, hash);
        if (!File.Exists(skinPath))
            await File.WriteAllBytesAsync(skinPath, skinBytes, cancellationToken);

        var existing = FindExisting(account.SkinLibrary, hash, skinModel);
        return existing is null
            ? CreateRecord(hash, skinModel, new Uri(skinPath).AbsoluteUri)
            : CopyRecordWithSource(existing, new Uri(skinPath).AbsoluteUri, hash);
    }

    public Task DeleteSkinAsync(
        LauncherAccount account,
        LauncherSkinRecord skin,
        CancellationToken cancellationToken)
    {
        // 仅删除确认位于账户缓存目录内的文件，外部来源路径绝不能随记录删除。
        var uuid = account.Uuid ?? account.Id;
        if (string.IsNullOrWhiteSpace(uuid))
            return Task.CompletedTask;

        var accountSkinDirectory = Path.GetFullPath(GetAccountSkinDirectory(uuid));
        if (!Directory.Exists(accountSkinDirectory))
            return Task.CompletedTask;

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sourcePath = ResolveSkinSourcePath(skin.Source);
        if (sourcePath is not null)
        {
            var fullSourcePath = Path.GetFullPath(sourcePath);
            if (IsPathInDirectory(fullSourcePath, accountSkinDirectory) && File.Exists(fullSourcePath))
                candidates.Add(fullSourcePath);
        }

        if (!string.IsNullOrWhiteSpace(skin.ContentHash))
        {
            foreach (var skinPath in Directory.EnumerateFiles(accountSkinDirectory, "*.png"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (string.Equals(
                        ComputeSkinContentHash(File.ReadAllBytes(skinPath)),
                        skin.ContentHash,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(Path.GetFullPath(skinPath));
                    }
                }
                catch
                {
                }
            }
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteFile(candidate);
        }

        return Task.CompletedTask;
    }
}
