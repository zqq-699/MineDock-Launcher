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
internal sealed class AccountSkinCacheService
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

    private string CreateSkinPath(string uuid, string contentHash)
    {
        var accountSkinDirectory = GetAccountSkinDirectory(uuid);
        Directory.CreateDirectory(accountSkinDirectory);
        var safeHash = contentHash.Length > 24 ? contentHash[..24] : contentHash;
        return Path.Combine(accountSkinDirectory, $"{SkinCacheVersion}-{safeHash}.png");
    }

    private string? GetLatestCachedSkinPath(string uuid)
    {
        if (string.IsNullOrWhiteSpace(uuid))
            return null;

        var accountSkinDirectory = GetAccountSkinDirectory(uuid);
        if (Directory.Exists(accountSkinDirectory))
        {
            var accountSkinPath = Directory.EnumerateFiles(accountSkinDirectory, "*.png")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;
            if (accountSkinPath is not null)
                return accountSkinPath;
        }

        if (!Directory.Exists(skinDirectory))
            return null;

        return Directory.EnumerateFiles(skinDirectory, $"{uuid}-{SkinCacheVersion}-*.png")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault()
            ?.FullName;
    }

    private string GetAccountSkinDirectory(string uuid)
    {
        return Path.Combine(skinDirectory, SanitizePathSegment(uuid));
    }

    private static LauncherSkinRecord? FindExisting(
        IReadOnlyList<LauncherSkinRecord> skins,
        string contentHash,
        MinecraftSkinModel skinModel)
    {
        return skins.FirstOrDefault(skin =>
            skin.SkinModel == skinModel
            && (string.Equals(skin.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase)
                || SourceContentMatchesHash(skin.Source, contentHash)));
    }

    private static LauncherSkinRecord? FindExistingBySource(
        IReadOnlyList<LauncherSkinRecord> skins,
        string skinPath)
    {
        return skins.FirstOrDefault(skin =>
            ResolveSkinSourcePath(skin.Source) is { } sourcePath
            && string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(skinPath),
                StringComparison.OrdinalIgnoreCase));
    }

    private static MinecraftSkinModel? FindModelForFile(
        IReadOnlyList<LauncherSkinRecord> skins,
        string skinPath,
        string contentHash)
    {
        var sourceMatch = FindExistingBySource(skins, skinPath);
        if (sourceMatch is not null)
            return sourceMatch.SkinModel;

        return skins.FirstOrDefault(skin =>
                string.Equals(skin.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            ?.SkinModel;
    }

    private static LauncherSkinRecord CreateRecord(
        string contentHash,
        MinecraftSkinModel skinModel,
        string source)
    {
        return new LauncherSkinRecord
        {
            Id = $"skin-{contentHash[..Math.Min(16, contentHash.Length)]}-{skinModel.ToString().ToLowerInvariant()}",
            Source = source,
            SkinModel = skinModel,
            ContentHash = contentHash,
            AddedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static LauncherSkinRecord CopyRecordWithSource(
        LauncherSkinRecord skin,
        string source,
        string contentHash)
    {
        return new LauncherSkinRecord
        {
            Id = skin.Id,
            Source = source,
            SkinModel = skin.SkinModel,
            ContentHash = string.IsNullOrWhiteSpace(skin.ContentHash) ? contentHash : skin.ContentHash,
            AddedAtUtc = skin.AddedAtUtc
        };
    }

    private static string? ResolveSkinSourcePath(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return null;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return uri.IsFile ? uri.LocalPath : null;

        return source;
    }

    private static bool SourceContentMatchesHash(string source, string contentHash)
    {
        // 哈希匹配需要读取实际文件，路径或 URL 字符串本身不能代表皮肤内容身份。
        var sourcePath = ResolveSkinSourcePath(source);
        if (sourcePath is null || !File.Exists(sourcePath))
            return false;

        try
        {
            return string.Equals(
                ComputeSkinContentHash(File.ReadAllBytes(sourcePath)),
                contentHash,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static MinecraftSkinModel? TryParseSkinModel(string skinPath)
    {
        // 文件名模型后缀只作为历史兼容提示，无法识别时交由调用方使用默认模型。
        var name = Path.GetFileNameWithoutExtension(skinPath);
        if (name.EndsWith("-slim", StringComparison.OrdinalIgnoreCase))
            return MinecraftSkinModel.Slim;

        if (name.EndsWith("-classic", StringComparison.OrdinalIgnoreCase))
            return MinecraftSkinModel.Classic;

        return null;
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray();
        return new string(chars);
    }

    private static bool IsPathInDirectory(string path, string directory)
    {
        var normalizedDirectory = directory.EndsWith(Path.DirectorySeparatorChar)
            ? directory
            : directory + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string ComputeSkinContentHash(byte[] bytes)
    {
        try
        {
            var bitmap = DecodeSkinBitmap(bytes);
            var stride = bitmap.PixelWidth * 4;
            var pixels = new byte[stride * bitmap.PixelHeight];
            bitmap.CopyPixels(pixels, stride, 0);

            var hashInput = new byte[8 + pixels.Length];
            BitConverter.GetBytes(bitmap.PixelWidth).CopyTo(hashInput, 0);
            BitConverter.GetBytes(bitmap.PixelHeight).CopyTo(hashInput, 4);
            pixels.CopyTo(hashInput, 8);
            return ComputeHash(hashInput);
        }
        catch
        {
            return ComputeHash(bytes);
        }
    }

    private static BitmapSource DecodeSkinBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();

        if (frame.Format == PixelFormats.Bgra32)
            return frame;

        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    private static string ComputeHash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
