/*
 * BlockHelm Launcher
 * Copyright (C) 2026 Quan Zhou
 *
 * SPDX-License-Identifier: GPL-3.0-only
 */

using System.IO;
using System.Text.Json;
using CmlLib.Core;
using CmlLib.Core.FileExtractors;
using CmlLib.Core.Files;
using CmlLib.Core.Rules;
using CmlLib.Core.Tasks;
using CmlLib.Core.Version;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Minecraft;

internal sealed class SafeAssetFileExtractor : IFileExtractor
{
    private readonly MinecraftDownloadRequestExecutor downloadExecutor;
    private readonly DownloadSourcePreference downloadSourcePreference;

    public SafeAssetFileExtractor(
        MinecraftDownloadRequestExecutor downloadExecutor,
        DownloadSourcePreference downloadSourcePreference)
    {
        this.downloadExecutor = downloadExecutor;
        this.downloadSourcePreference = downloadSourcePreference;
    }

    public async ValueTask<IEnumerable<GameFile>> Extract(
        MinecraftPath path,
        IVersion version,
        RulesEvaluatorContext rulesContext,
        CancellationToken cancellationToken)
    {
        var metadata = version.AssetIndex;
        if (metadata is null || string.IsNullOrWhiteSpace(metadata.Id))
            return [];

        var indexPath = MinecraftPathGuard.EnsureWithin(
            path.GetIndexFilePath(metadata.Id),
            path.Assets,
            "Asset index");
        if (!IsValidFile(indexPath, metadata.GetSha1(), metadata.Size))
        {
            if (string.IsNullOrWhiteSpace(metadata.Url))
                throw new InvalidDataException($"Asset index URL is missing: {metadata.Id}");

            await downloadExecutor.DownloadFileAsync(
                metadata.Url,
                downloadSourcePreference,
                categoryHint: "Mojang",
                indexPath,
                metadata.GetSha1(),
                metadata.Size > 0 ? metadata.Size : null,
                reportDownloadedBytes: null,
                cancellationToken).ConfigureAwait(false);
        }

        await using var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var assetIndex = new JsonAssetIndex(metadata.Id, document);
        return CreateFiles(assetIndex, path);
    }

    private static IEnumerable<GameFile> CreateFiles(IAssetIndex assetIndex, MinecraftPath path)
    {
        try
        {
            foreach (var item in assetIndex.EnumerateAssetObjects())
            {
                var relativeObjectPath = $"{item.Hash[..2]}/{item.Hash}";
                var objectPath = MinecraftPathGuard.EnsureWithin(
                    Path.Combine(path.GetAssetObjectPath(assetIndex.Id), relativeObjectPath),
                    path.Assets,
                    "Asset object");
                var updateTasks = new List<IUpdateTask>(2);
                if (assetIndex.IsVirtual)
                {
                    updateTasks.Add(new AtomicAssetCopyTask(
                        MinecraftPathGuard.EnsureWithin(
                            Path.Combine(path.GetAssetLegacyPath(assetIndex.Id), item.Name),
                            path.Assets,
                            "Virtual asset"),
                        item.Hash));
                }
                if (assetIndex.MapToResources)
                {
                    updateTasks.Add(new AtomicAssetCopyTask(
                        MinecraftPathGuard.EnsureWithin(
                            Path.Combine(path.Resource, item.Name),
                            path.Resource,
                            "Legacy resource"),
                        item.Hash));
                }

                yield return new GameFile(item.Name)
                {
                    Path = objectPath,
                    Hash = item.Hash,
                    Size = item.Size,
                    Url = $"https://resources.download.minecraft.net/{relativeObjectPath}",
                    UpdateTask = updateTasks
                };
            }
        }
        finally
        {
            if (assetIndex is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static bool IsValidFile(string path, string? expectedSha1, long expectedSize)
    {
        if (!File.Exists(path))
            return false;
        if (expectedSize > 0 && new FileInfo(path).Length != expectedSize)
            return false;
        if (string.IsNullOrWhiteSpace(expectedSha1))
            return true;
        return string.Equals(
            AtomicSharedFilePublisher.ComputeSha1(path),
            expectedSha1,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class AtomicAssetCopyTask(string destinationPath, string expectedSha1) : IUpdateTask
    {
        public ValueTask Execute(GameFile file, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(file.Path))
                throw new InvalidDataException("Asset object path is missing.");

            return new ValueTask(AtomicSharedFilePublisher.PublishCopyAsync(
                file.Path,
                destinationPath,
                expectedSha1,
                cancellationToken));
        }
    }
}
