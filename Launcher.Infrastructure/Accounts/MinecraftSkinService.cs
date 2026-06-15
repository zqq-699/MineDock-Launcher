using Launcher.Application.Accounts;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftSkinService
{
    private readonly MicrosoftAuthProvider authProvider;
    private readonly MinecraftProfileClient profileClient;
    private readonly MicrosoftAccountFactory accountFactory;

    public MinecraftSkinService(
        MicrosoftAuthProvider authProvider,
        MinecraftProfileClient profileClient,
        MicrosoftAccountFactory accountFactory)
    {
        this.authProvider = authProvider;
        this.profileClient = profileClient;
        this.accountFactory = accountFactory;
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
        return await accountFactory.CreateAccountFromProfileAsync(profile, forceRefreshAvatar: true, cancellationToken);
    }
}
