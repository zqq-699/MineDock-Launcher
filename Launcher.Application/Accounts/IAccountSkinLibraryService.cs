using Launcher.Domain.Models;

namespace Launcher.Application.Accounts;

public interface IAccountSkinLibraryService
{
    IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account);

    Task<LauncherSkinRecord> ImportSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken = default);

    Task DeleteSkinAsync(
        LauncherAccount account,
        LauncherSkinRecord skin,
        CancellationToken cancellationToken = default);
}
