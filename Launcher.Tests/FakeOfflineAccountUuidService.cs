using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Tests;

internal sealed class FakeOfflineAccountUuidService : IOfflineAccountUuidService
{
    public string CreateUuid(
        string username,
        OfflineUuidGenerationMode mode,
        string? existingUuid = null)
    {
        return (mode == OfflineUuidGenerationMode.Random || mode == OfflineUuidGenerationMode.Manual)
            && !string.IsNullOrWhiteSpace(existingUuid)
            ? existingUuid
            : $"{mode}-{username}";
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
}
