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

internal sealed class AccountSkinCacheService
{
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
