using System.Security.Cryptography;
using System.Text;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

public sealed class OfflineAccountUuidService : IOfflineAccountUuidService
{
    public string CreateUuid(
        string username,
        OfflineUuidGenerationMode mode,
        string? existingUuid = null)
    {
        return mode switch
        {
            OfflineUuidGenerationMode.Random => CreateRandomUuid(existingUuid),
            OfflineUuidGenerationMode.Manual => CreateManualUuid(username, existingUuid),
            _ => CreateStandardOfflineUuid(username)
        };
    }

    public bool TryNormalizeUuid(string input, out string normalizedUuid)
    {
        if (Guid.TryParse(input, out var parsed))
        {
            normalizedUuid = parsed.ToString();
            return true;
        }

        var compact = input.Replace("-", string.Empty, StringComparison.Ordinal);
        if (Guid.TryParseExact(compact, "N", out parsed))
        {
            normalizedUuid = parsed.ToString();
            return true;
        }

        normalizedUuid = string.Empty;
        return false;
    }

    private static string CreateRandomUuid(string? existingUuid)
    {
        return string.IsNullOrWhiteSpace(existingUuid)
            ? Guid.NewGuid().ToString()
            : NormalizeUuid(existingUuid);
    }

    private string CreateManualUuid(string username, string? existingUuid)
    {
        return TryNormalizeUuid(existingUuid ?? string.Empty, out var normalizedUuid)
            ? normalizedUuid
            : CreateStandardOfflineUuid(username);
    }

    private static string CreateStandardOfflineUuid(string username)
    {
        var input = Encoding.UTF8.GetBytes($"OfflinePlayer:{username}");
        var hash = MD5.HashData(input);
        hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3f) | 0x80);

        return string.Join(
            "-",
            Convert.ToHexString(hash, 0, 4),
            Convert.ToHexString(hash, 4, 2),
            Convert.ToHexString(hash, 6, 2),
            Convert.ToHexString(hash, 8, 2),
            Convert.ToHexString(hash, 10, 6)).ToLowerInvariant();
    }

    private static string NormalizeUuid(string uuid)
    {
        if (Guid.TryParse(uuid, out var parsed))
            return parsed.ToString();

        var compact = uuid.Replace("-", string.Empty, StringComparison.Ordinal);
        return Guid.TryParseExact(compact, "N", out parsed)
            ? parsed.ToString()
            : uuid;
    }
}
