using Launcher.Application.Accounts;
using Launcher.Domain.Models;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftSkinService
{
    private readonly MicrosoftAuthProvider authProvider;
    private readonly MinecraftProfileClient profileClient;
    private readonly MicrosoftAccountFactory accountFactory;
    private readonly AccountSkinCacheService skinCacheService;

    public MinecraftSkinService(
        MicrosoftAuthProvider authProvider,
        MinecraftProfileClient profileClient,
        MicrosoftAccountFactory accountFactory,
        AccountSkinCacheService skinCacheService)
    {
        this.authProvider = authProvider;
        this.profileClient = profileClient;
        this.accountFactory = accountFactory;
        this.skinCacheService = skinCacheService;
    }

    public async Task<LauncherAccount> UploadSkinAsync(
        LauncherAccount account,
        string skinFilePath,
        MinecraftSkinModel skinModel,
        CancellationToken cancellationToken)
    {
        var accessToken = await authProvider.GetAccessTokenAsync(account, cancellationToken);
        await profileClient.UploadSkinAsync(accessToken, skinFilePath, skinModel, cancellationToken);

        var profile = await profileClient.GetProfileAsync(accessToken, cancellationToken);
        var uuid = MinecraftAccountHelpers.NormalizeUuid(profile.Id);
        var skinSource = await skinCacheService.StoreUploadedSkinAsync(
            uuid,
            skinFilePath,
            skinModel,
            cancellationToken);
        var updatedAccount = await accountFactory.CreateAccountFromProfileAsync(
            profile,
            forceRefreshAvatar: true,
            cancellationToken,
            account.SkinLibrary);

        return new LauncherAccount
        {
            Id = updatedAccount.Id,
            DisplayName = updatedAccount.DisplayName,
            Uuid = updatedAccount.Uuid,
            OfflineUuidGenerationMode = updatedAccount.OfflineUuidGenerationMode,
            AvatarSource = updatedAccount.AvatarSource,
            SkinSource = skinSource ?? updatedAccount.SkinSource,
            SkinModel = skinModel,
            IsOffline = updatedAccount.IsOffline,
            HasFreshProfile = updatedAccount.HasFreshProfile,
            CachedCapeOptions = updatedAccount.CachedCapeOptions
        };
    }
}
