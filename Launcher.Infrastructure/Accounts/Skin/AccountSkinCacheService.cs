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

/// <summary>
/// 以内容哈希持久化账户皮肤，兼容历史缓存路径，并维护账户皮肤记录与实际文件的一致性。
/// </summary>
internal sealed partial class AccountSkinCacheService
{
    // 以 UUID 分目录隔离账户，以内容哈希命名避免相同皮肤重复写入并支持可靠去重。
    private const string SkinCacheVersion = "v1";

    private readonly HttpClient httpClient;
    private readonly string skinDirectory;

    public AccountSkinCacheService(HttpClient httpClient, LauncherPathProvider pathProvider)
        : this(
            httpClient,
            Path.Combine(pathProvider.DefaultAccountDataDirectory, "microsoft", "skins"))
    {
    }

    internal AccountSkinCacheService(HttpClient httpClient, string skinDirectory)
    {
        this.httpClient = httpClient;
        this.skinDirectory = skinDirectory;
        Directory.CreateDirectory(this.skinDirectory);
    }

    public async Task<string?> GetOrCreateSkinSourceAsync(
        string uuid,
        string? skinUrl,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        // 优先复用账户记录指向的可用文件；只有缺失时才下载当前服务端皮肤。
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var cachedSkinPath = GetLatestCachedSkinPath(uuid);
        if (!forceRefresh)
            return cachedSkinPath is null ? null : new Uri(cachedSkinPath).AbsoluteUri;

        if (string.IsNullOrWhiteSpace(skinUrl))
            return cachedSkinPath is null ? null : new Uri(cachedSkinPath).AbsoluteUri;

        try
        {
            var skinBytes = await httpClient.GetByteArrayAsync(skinUrl, cancellationToken);
            var hash = ComputeSkinContentHash(skinBytes);
            var skinPath = CreateSkinPath(uuid, hash);
            if (!File.Exists(skinPath))
                await File.WriteAllBytesAsync(skinPath, skinBytes, cancellationToken);
            return new Uri(skinPath).AbsoluteUri;
        }
        catch
        {
            return cachedSkinPath is null ? null : new Uri(cachedSkinPath).AbsoluteUri;
        }
    }

    public async Task<string?> StoreUploadedSkinAsync(
        string uuid,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken)
    {
        // 上传响应字节是最终内容真相，先验证可解码再按哈希原子落盘。
        if (string.IsNullOrWhiteSpace(uuid) || string.IsNullOrWhiteSpace(skinFilePath) || !File.Exists(skinFilePath))
            return GetLatestCachedSkinPath(uuid) is { } cachedSkinPath
                ? new Uri(cachedSkinPath).AbsoluteUri
                : null;

        var skinBytes = await File.ReadAllBytesAsync(skinFilePath, cancellationToken);
        var hash = ComputeSkinContentHash(skinBytes);
        var skinPath = CreateSkinPath(uuid, hash);
        if (!File.Exists(skinPath))
            await File.WriteAllBytesAsync(skinPath, skinBytes, cancellationToken);
        return new Uri(skinPath).AbsoluteUri;
    }
}
