using System.IO;
using System.Windows.Media.Imaging;
using Launcher.Application.Accounts;

namespace Launcher.Infrastructure.Accounts;

internal sealed class MinecraftSkinFileValidator : IMinecraftSkinFileValidator
{
    public Task<MinecraftSkinFileValidationResult> ValidateAsync(
        string skinFilePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(skinFilePath))
            return Task.FromResult(new MinecraftSkinFileValidationResult(false, 0, 0));

        try
        {
            using var stream = File.OpenRead(skinFilePath);
            var decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.FirstOrDefault();
            var width = frame?.PixelWidth ?? 0;
            var height = frame?.PixelHeight ?? 0;
            var isValid = width == 64 && (height == 64 || height == 32);
            return Task.FromResult(new MinecraftSkinFileValidationResult(isValid, width, height));
        }
        catch
        {
            return Task.FromResult(new MinecraftSkinFileValidationResult(false, 0, 0));
        }
    }
}
