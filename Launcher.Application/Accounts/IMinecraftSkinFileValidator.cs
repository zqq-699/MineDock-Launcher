namespace Launcher.Application.Accounts;

public interface IMinecraftSkinFileValidator
{
    Task<MinecraftSkinFileValidationResult> ValidateAsync(
        string skinFilePath,
        CancellationToken cancellationToken = default);
}
