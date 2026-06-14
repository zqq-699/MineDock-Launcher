using Launcher.Application.Accounts;
using System.Net.Http;

namespace Launcher.Infrastructure.Accounts;

public sealed class MicrosoftAccountService : IMicrosoftAccountService
{
    private static readonly HttpClient HttpClient = new();

    private readonly MicrosoftAuthProvider authProvider;
    private readonly AccountAvatarService avatarService;
    private readonly MinecraftProfileClient profileClient;
    private readonly MicrosoftAccountFactory accountFactory;
    private readonly MinecraftSkinService skinService;
    private readonly MinecraftCapeService capeService;

    public MicrosoftAccountService()
    {
        authProvider = new MicrosoftAuthProvider();
        avatarService = new AccountAvatarService(HttpClient);
        profileClient = new MinecraftProfileClient(HttpClient);
        accountFactory = new MicrosoftAccountFactory(avatarService);
        skinService = new MinecraftSkinService(authProvider, profileClient, accountFactory);
        capeService = new MinecraftCapeService(authProvider, profileClient);
    }

    public async Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = new List<LauncherAccount>();
        foreach (var account in authProvider.GetSavedAccounts())
        {
            var profile = account.Profile;
            if (profile is null
                || string.IsNullOrWhiteSpace(profile.Username)
                || string.IsNullOrWhiteSpace(profile.UUID))
            {
                continue;
            }

            accounts.Add(await accountFactory.CreateAccountFromProfileAsync(
                profile,
                forceRefreshAvatar: false,
                cancellationToken));
        }

        return accounts;
    }

    public async Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
    {
        var login = await authProvider.LoginInteractivelyAsync(cancellationToken);
        if (login.Profile is not null
            && !string.IsNullOrWhiteSpace(login.Profile.Username)
            && !string.IsNullOrWhiteSpace(login.Profile.UUID))
        {
            return await accountFactory.CreateAccountFromProfileAsync(
                login.Profile,
                forceRefreshAvatar: true,
                cancellationToken);
        }

        var uuid = MinecraftAccountHelpers.NormalizeUuid(login.Uuid);
        return new LauncherAccount
        {
            Id = $"microsoft-{uuid}",
            DisplayName = login.Username ?? string.Empty,
            Uuid = uuid,
            IsOffline = false
        };
    }

    public async Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default)
    {
        var deleted = await authProvider.DeleteAccountAsync(account, cancellationToken);
        if (deleted && !string.IsNullOrWhiteSpace(account.Uuid))
            avatarService.DeleteAvatar(account.Uuid);
    }

    public Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        return capeService.GetCapesAsync(account, cancellationToken);
    }

    public Task<LauncherAccount> UploadSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        CancellationToken cancellationToken = default)
    {
        return skinService.UploadSkinAsync(account, skinFilePath, cancellationToken);
    }

    public Task SetActiveCapeAsync(
        LauncherAccount account,
        string? capeId,
        CancellationToken cancellationToken = default)
    {
        return capeService.SetActiveCapeAsync(account, capeId, cancellationToken);
    }

    public async Task<LauncherAccount> ChangeNameAsync(
        LauncherAccount account,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
        var profile = await profileClient.ChangeNameAsync(accessToken, newName, cancellationToken);
        return await accountFactory.CreateAccountFromProfileAsync(
            profile,
            forceRefreshAvatar: true,
            cancellationToken);
    }
}
