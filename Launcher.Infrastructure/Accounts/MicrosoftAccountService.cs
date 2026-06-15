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
        var pathProvider = new LauncherPathProvider();
        authProvider = new MicrosoftAuthProvider(pathProvider);
        avatarService = new AccountAvatarService(HttpClient, pathProvider);
        profileClient = new MinecraftProfileClient(HttpClient);
        accountFactory = new MicrosoftAccountFactory(avatarService);
        skinService = new MinecraftSkinService(authProvider, profileClient, accountFactory);
        capeService = new MinecraftCapeService(authProvider, profileClient);
    }

    public async Task<IReadOnlyList<LauncherAccount>> GetSavedAccountsAsync(CancellationToken cancellationToken = default)
    {
        var accounts = new List<LauncherAccount>();
        foreach (var savedAccount in authProvider.GetSavedAccounts())
        {
            var profile = savedAccount.Profile;
            if (profile is null
                || string.IsNullOrWhiteSpace(profile.Username)
                || string.IsNullOrWhiteSpace(profile.UUID))
            {
                continue;
            }

            var cachedAccount = await accountFactory.CreateAccountFromProfileAsync(
                profile,
                forceRefreshAvatar: false,
                cancellationToken);
            accounts.Add(cachedAccount);
        }

        return accounts;
    }

    public async Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
    {
        var login = await authProvider.LoginInteractivelyAsync(cancellationToken);
        var currentProfile = await TryGetCurrentProfileAsync(login, cancellationToken);
        if (currentProfile is not null)
        {
            var account = await accountFactory.CreateAccountFromProfileAsync(
                currentProfile,
                forceRefreshAvatar: true,
                cancellationToken);
            authProvider.UpdateSavedProfile(
                account,
                account.DisplayName,
                account.Uuid ?? string.Empty);
            return account;
        }

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

    public async Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await capeService.GetCapesAsync(account, cancellationToken);
        }
        catch (MinecraftProfileRequestException ex)
        {
            throw new MicrosoftAccountProfileRefreshException(ex.ErrorCode, ex);
        }
    }

    public async Task<LauncherAccount> RefreshAccountProfileAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await RefreshSavedAccountAsync(account, cancellationToken);
        }
        catch (MinecraftProfileRequestException ex)
        {
            throw new MicrosoftAccountProfileRefreshException(ex.ErrorCode, ex);
        }
    }

    public async Task<LauncherAccount> UploadSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await skinService.UploadSkinAsync(account, skinFilePath, skinModel, cancellationToken);
        }
        catch (MinecraftProfileRequestException ex)
        {
            throw new MicrosoftAccountSkinUpdateException(ex.ErrorCode, ex);
        }
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
        try
        {
            var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
            var profile = await profileClient.ChangeNameAsync(accessToken, newName, cancellationToken);
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? newName.Trim() : profile.Name;
            var uuid = MinecraftAccountHelpers.NormalizeUuid(profile.Id);
            authProvider.UpdateSavedProfile(
                account,
                profile.Name,
                uuid);

            return await accountFactory.CreateAccountFromProfileAsync(
                profile,
                forceRefreshAvatar: true,
                cancellationToken);
        }
        catch (MinecraftProfileRequestException ex)
        {
            throw new MicrosoftAccountNameChangeException(
                MapNameChangeFailure(ex.ErrorKind),
                ex.ErrorCode,
                ex);
        }
    }

    private static MicrosoftAccountNameChangeFailureReason MapNameChangeFailure(MinecraftProfileErrorKind errorKind)
    {
        return errorKind switch
        {
            MinecraftProfileErrorKind.Duplicate => MicrosoftAccountNameChangeFailureReason.DuplicateName,
            MinecraftProfileErrorKind.NotAllowed => MicrosoftAccountNameChangeFailureReason.NotAllowed,
            MinecraftProfileErrorKind.ConstraintViolation => MicrosoftAccountNameChangeFailureReason.InvalidName,
            _ => MicrosoftAccountNameChangeFailureReason.Unknown
        };
    }

    private async Task<MinecraftProfileResponse?> TryGetCurrentProfileAsync(
        MicrosoftLoginResult login,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(login.AccessToken))
            return null;

        try
        {
            return await profileClient.GetProfileAsync(login.AccessToken, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<LauncherAccount> RefreshSavedAccountAsync(
        LauncherAccount account,
        CancellationToken cancellationToken)
    {
        var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
        var profile = await profileClient.GetProfileAsync(accessToken, cancellationToken);
        var refreshedAccount = await accountFactory.CreateAccountFromProfileAsync(
            profile,
            forceRefreshAvatar: true,
            cancellationToken);
        authProvider.UpdateSavedProfile(
            refreshedAccount,
            refreshedAccount.DisplayName,
            refreshedAccount.Uuid ?? string.Empty);
        return refreshedAccount;
    }
}
