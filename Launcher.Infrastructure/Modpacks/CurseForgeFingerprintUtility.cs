using System.Buffers.Binary;
using System.IO;

namespace Launcher.Infrastructure.Modpacks;

internal static class CurseForgeFingerprintUtility
{
    public static async Task<long> ComputeFileFingerprintAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 81920,
            useAsync: true);
        using var fingerprintBytes = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;

            for (var i = 0; i < read; i++)
            {
                var value = buffer[i];
                if (value is not (0x09 or 0x0a or 0x0d or 0x20))
                    fingerprintBytes.WriteByte(value);
            }
        }

        return ComputeMurmurHash2(fingerprintBytes.GetBuffer().AsSpan(0, (int)fingerprintBytes.Length));
    }

    private static long ComputeMurmurHash2(ReadOnlySpan<byte> data)
    {
        const uint seed = 1;
        const uint m = 0x5bd1e995;
        const int r = 24;

        var length = data.Length;
        var hash = seed ^ (uint)length;
        var current = data;
        while (current.Length >= 4)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(current);
            value *= m;
            value ^= value >> r;
            value *= m;

            hash *= m;
            hash ^= value;
            current = current[4..];
        }

        switch (current.Length)
        {
            case 3:
                hash ^= (uint)current[2] << 16;
                goto case 2;
            case 2:
                hash ^= (uint)current[1] << 8;
                goto case 1;
            case 1:
                hash ^= current[0];
                hash *= m;
                break;
        }

        hash ^= hash >> 13;
        hash *= m;
        hash ^= hash >> 15;
        return hash;
    }
}
