using Launcher.Application.Accounts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly ILogger<MicrosoftAccountService> logger;

    public MicrosoftAccountService(ILogger<MicrosoftAccountService>? logger = null)
    {
        this.logger = logger ?? NullLogger<MicrosoftAccountService>.Instance;
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

        logger.LogInformation("Saved Microsoft accounts loaded. Count={AccountCount}", accounts.Count);
        return accounts;
    }

    public async Task<LauncherAccount> LoginInteractivelyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Interactive Microsoft account login started.");
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
                logger.LogInformation("Interactive Microsoft account login completed with current profile. AccountId={AccountId}", account.Id);
                return account;
            }

            if (login.Profile is not null
                && !string.IsNullOrWhiteSpace(login.Profile.Username)
                && !string.IsNullOrWhiteSpace(login.Profile.UUID))
            {
                var account = await accountFactory.CreateAccountFromProfileAsync(
                    login.Profile,
                    forceRefreshAvatar: true,
                    cancellationToken);
                logger.LogInformation("Interactive Microsoft account login completed with saved profile. AccountId={AccountId}", account.Id);
                return account;
            }

            var uuid = MinecraftAccountHelpers.NormalizeUuid(login.Uuid);
            var fallbackAccount = new LauncherAccount
            {
                Id = $"microsoft-{uuid}",
                DisplayName = login.Username ?? string.Empty,
                Uuid = uuid,
                IsOffline = false
            };
            logger.LogInformation("Interactive Microsoft account login completed with fallback profile. AccountId={AccountId}", fallbackAccount.Id);
            return fallbackAccount;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Interactive Microsoft account login failed.");
            throw;
        }
    }

    public async Task DeleteAccountAsync(LauncherAccount account, CancellationToken cancellationToken = default)
    {
        var deleted = await authProvider.DeleteAccountAsync(account, cancellationToken);
        if (deleted && !string.IsNullOrWhiteSpace(account.Uuid))
            avatarService.DeleteAvatar(account.Uuid);
        logger.LogInformation("Microsoft account delete requested. AccountId={AccountId} Deleted={Deleted}", account.Id, deleted);
    }

    public async Task<IReadOnlyList<AccountCapeOption>> GetCapesAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var capes = await capeService.GetCapesAsync(account, cancellationToken);
            logger.LogInformation("Microsoft account capes loaded. AccountId={AccountId} CapeCount={CapeCount}", account.Id, capes.Count);
            return capes;
        }
        catch (MinecraftProfileRequestException ex)
        {
            logger.LogWarning(ex, "Microsoft account capes load failed. AccountId={AccountId} ErrorCode={ErrorCode}", account.Id, ex.ErrorCode);
            throw new MicrosoftAccountProfileRefreshException(ex.ErrorCode, ex);
        }
    }

    public async Task<LauncherAccount> RefreshAccountProfileAsync(
        LauncherAccount account,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var refreshed = await RefreshSavedAccountAsync(account, cancellationToken);
            logger.LogInformation("Microsoft account profile refreshed. AccountId={AccountId}", refreshed.Id);
            return refreshed;
        }
        catch (MinecraftProfileRequestException ex)
        {
            logger.LogWarning(ex, "Microsoft account profile refresh failed. AccountId={AccountId} ErrorCode={ErrorCode}", account.Id, ex.ErrorCode);
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
            var updated = await skinService.UploadSkinAsync(account, skinFilePath, skinModel, cancellationToken);
            logger.LogInformation("Microsoft account skin uploaded. AccountId={AccountId} SkinModel={SkinModel}", updated.Id, skinModel);
            return updated;
        }
        catch (MinecraftProfileRequestException ex)
        {
            logger.LogWarning(ex, "Microsoft account skin upload failed. AccountId={AccountId} SkinModel={SkinModel} ErrorCode={ErrorCode}", account.Id, skinModel, ex.ErrorCode);
            throw new MicrosoftAccountSkinUpdateException(ex.ErrorCode, ex);
        }
    }

    public async Task SetActiveCapeAsync(
        LauncherAccount account,
        string? capeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogInformation("Microsoft account active cape update requested. AccountId={AccountId} HasCape={HasCape}", account.Id, !string.IsNullOrWhiteSpace(capeId));
            await capeService.SetActiveCapeAsync(account, capeId, cancellationToken);
            logger.LogInformation("Microsoft account active cape updated. AccountId={AccountId} HasCape={HasCape}", account.Id, !string.IsNullOrWhiteSpace(capeId));
        }
        catch (MinecraftProfileRequestException ex)
        {
            logger.LogWarning(ex, "Microsoft account active cape update failed. AccountId={AccountId} HasCape={HasCape} ErrorCode={ErrorCode}", account.Id, !string.IsNullOrWhiteSpace(capeId), ex.ErrorCode);
            throw new MicrosoftAccountProfileRefreshException(ex.ErrorCode, ex);
        }
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

            var updatedAccount = await accountFactory.CreateAccountFromProfileAsync(
                profile,
                forceRefreshAvatar: true,
                cancellationToken);
            logger.LogInformation("Microsoft account name changed. AccountId={AccountId}", updatedAccount.Id);
            return updatedAccount;
        }
        catch (MinecraftProfileRequestException ex)
        {
            logger.LogWarning(
                ex,
                "Microsoft account name change failed. AccountId={AccountId} Reason={Reason} ErrorCode={ErrorCode}",
                account.Id,
                MapNameChangeFailure(ex.ErrorKind),
                ex.ErrorCode);
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
