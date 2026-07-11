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

using System.Buffers;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace Launcher.Infrastructure.Minecraft;

/// <summary>
/// 将响应写入同目录临时文件，验证长度与哈希后再原子替换正式下载目标。
/// </summary>
internal static class MinecraftDownloadFileWriter
{
    private const int DownloadBufferSize = 81920;

    public static void PrepareDestination(string destinationPath, string? expectedSha1)
    {
        ValidateExpectedSha1(expectedSha1);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new InvalidOperationException($"Download destination has no parent directory: {destinationPath}"));
    }

    /// <summary>
    /// 流式写入响应、持续报告进度，并在完整性验证成功后提交临时文件。
    /// </summary>
    public static async Task WriteAsync(
        HttpResponseMessage response,
        string destinationPath,
        string? expectedSha1,
        long? expectedSize,
        Action<long>? reportDownloadedBytes,
        int attemptNumber,
        Action<int, long, long?>? reportAttemptProgress,
        CancellationToken cancellationToken)
    {
        // 临时文件与目标位于同一目录，最终 Move 不跨卷且失败前不会破坏已有文件。
        var tempPath = Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        var committed = false;

        try
        {
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var destination = OpenTempFile(tempPath);
            using var sha1 = string.IsNullOrWhiteSpace(expectedSha1)
                ? null
                : IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            var buffer = ArrayPool<byte>.Shared.Rent(DownloadBufferSize);
            long totalRead = 0;

            try
            {
                while (true)
                {
                    var read = await ReadNetworkAsync(source, buffer, cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    await WriteLocalAsync(destination, tempPath, buffer, read, cancellationToken).ConfigureAwait(false);
                    sha1?.AppendData(buffer, 0, read);
                    totalRead += read;
                    reportDownloadedBytes?.Invoke(read);
                    reportAttemptProgress?.Invoke(attemptNumber, totalRead, expectedSize);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            // 必须在提交前完成落盘、长度和哈希验证；任何失败都只清理临时文件。
            await FlushAndCloseAsync(destination, tempPath, cancellationToken).ConfigureAwait(false);
            ValidateLength(response, expectedSize, totalRead);
            ValidateHash(destinationPath, expectedSha1, sha1);
            Commit(tempPath, destinationPath);
            committed = true;
        }
        finally
        {
            if (!committed)
                TryDeleteTempFile(tempPath);
        }
    }

    /// <summary>
    /// 读取响应体并区分用户取消与可重试的网络中断。
    /// </summary>
    private static async Task<int> ReadNetworkAsync(
        Stream source,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        try
        {
            return await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (DownloadAttemptException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException or OperationCanceledException)
        {
            throw new DownloadBodyInterruptedException(
                "The response body was interrupted while downloading a file.",
                exception);
        }
    }

    private static async Task WriteLocalAsync(
        Stream destination,
        string tempPath,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        try
        {
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DownloadLocalFileException(
                $"Failed to write the download temporary file: {tempPath}",
                exception);
        }
    }

    private static async Task FlushAndCloseAsync(
        FileStream destination,
        string tempPath,
        CancellationToken cancellationToken)
    {
        try
        {
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            await destination.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DownloadLocalFileException(
                $"Failed to flush the download temporary file: {tempPath}",
                exception);
        }
    }

    /// <summary>
    /// 使用请求期望大小或 Content-Length 检测被静默截断的响应体。
    /// </summary>
    private static void ValidateLength(
        HttpResponseMessage response,
        long? expectedSize,
        long totalRead)
    {
        var requiredSize = expectedSize is > 0
            ? expectedSize
            : response.Content.Headers.ContentLength;
        if (requiredSize is > 0 && totalRead != requiredSize.Value)
        {
            throw new DownloadBodyInterruptedException(
                $"The response body length was {totalRead}, expected {requiredSize.Value}.");
        }
    }

    /// <summary>
    /// 在启用 SHA-1 校验时比较流式计算结果并拒绝内容错误的文件。
    /// </summary>
    private static void ValidateHash(
        string destinationPath,
        string? expectedSha1,
        IncrementalHash? sha1)
    {
        if (sha1 is null)
            return;

        var actualSha1 = Convert.ToHexString(sha1.GetHashAndReset());
        if (!string.Equals(actualSha1, expectedSha1, StringComparison.OrdinalIgnoreCase))
        {
            throw new DownloadHashMismatchException(
                $"The downloaded file SHA-1 did not match the expected value for {destinationPath}.");
        }
    }

    /// <summary>
    /// 用已验证临时文件替换最终目标，并把本地文件错误转换为不可换源修复的异常。
    /// </summary>
    private static void Commit(string tempPath, string destinationPath)
    {
        try
        {
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DownloadLocalFileException(
                $"Failed to replace the download destination: {destinationPath}",
                exception);
        }
    }

    private static FileStream OpenTempFile(string tempPath)
    {
        try
        {
            return new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                DownloadBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new DownloadLocalFileException(
                $"Failed to create the download temporary file: {tempPath}",
                exception);
        }
    }

    private static void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void ValidateExpectedSha1(string? expectedSha1)
    {
        if (string.IsNullOrWhiteSpace(expectedSha1))
            return;

        if (expectedSha1.Length != 40 || expectedSha1.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The expected SHA-1 value is invalid.");
    }
}
