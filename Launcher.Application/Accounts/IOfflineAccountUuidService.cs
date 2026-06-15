using Launcher.Domain.Models;

namespace Launcher.Application.Accounts;

public interface IOfflineAccountUuidService
{
    string CreateUuid(
        string username,
        OfflineUuidGenerationMode mode,
        string? existingUuid = null);

    bool TryNormalizeUuid(string input, out string normalizedUuid);
}
