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
using System.Windows.Media.Imaging;

namespace Launcher.Infrastructure.FileSystem;

internal static class RemoteIconImageEncoder
{
    public static async Task<byte[]> ReadLimitedAsync(
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return buffer.ToArray();

            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("Remote local mod icon exceeds the maximum allowed size.");

            buffer.Write(chunk, 0, read);
        }
    }

    public static void SaveAsPng(ReadOnlyMemory<byte> imageBytes, string path)
    {
        using var source = new MemoryStream(imageBytes.ToArray(), writable: false);
        var decoder = BitmapDecoder.Create(
            source,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.FirstOrDefault()
            ?? throw new InvalidDataException("Remote local mod icon contains no frames.");
        frame.Freeze();

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame, null, null, null));
        using var output = File.Create(path);
        encoder.Save(output);
    }
}
