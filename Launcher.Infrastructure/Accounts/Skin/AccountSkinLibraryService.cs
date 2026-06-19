using System.Net.Http;
using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

public sealed class AccountSkinLibraryService : IAccountSkinLibraryService
{
    private static readonly HttpClient HttpClient = new();
    private readonly AccountSkinCacheService skinCacheService;

    public AccountSkinLibraryService()
    {
        skinCacheService = new AccountSkinCacheService(HttpClient, new LauncherPathProvider());
    }

    public IReadOnlyList<LauncherSkinRecord> GetAvailableSkins(LauncherAccount account)
    {
        return skinCacheService.GetAvailableSkins(account);
    }

    public Task<LauncherSkinRecord> ImportSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken = default)
    {
        return skinCacheService.ImportSkinAsync(account, skinFilePath, skinModel, cancellationToken);
    }

    public Task DeleteSkinAsync(
        LauncherAccount account,
        LauncherSkinRecord skin,
        CancellationToken cancellationToken = default)
    {
        return skinCacheService.DeleteSkinAsync(account, skin, cancellationToken);
    }
}
