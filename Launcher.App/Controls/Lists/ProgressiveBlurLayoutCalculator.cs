using System.Windows;
using Launcher.App.Effects;

namespace Launcher.App.Controls;

internal readonly record struct ProgressiveBlurRenderLayout(
    double LowResolutionWidth,
    double LowResolutionHeight,
    double UpscaleX,
    double UpscaleY,
    double ScaledBlurLength,
    double HorizontalMaximumRadius,
    double VerticalMaximumRadius,
    double TextureHeight,
    double PresentationHeight,
    double DirectListStart);

internal static class ProgressiveBlurLayoutCalculator
{
    internal static ProgressiveBlurRenderLayout Calculate(
        double width,
        double height,
        double blurLength,
        double visibleBlurBandHeight,
        double maximumRadius,
        double renderScale,
        DpiScale dpiScale)
    {
        var seamY = Math.Clamp(
            AlignToDevicePixel(visibleBlurBandHeight, dpiScale.DpiScaleY),
            0d,
            height);
        var textureHeight = Math.Min(
            height,
            seamY + ProgressiveBlurDefaults.TextureOverscanLength);
        var lowResolutionWidth = CalculateLowResolutionDimension(width, dpiScale.DpiScaleX, renderScale);
        var lowResolutionHeight = CalculateLowResolutionDimension(textureHeight, dpiScale.DpiScaleY, renderScale);
        var horizontalRatio = lowResolutionWidth / width;
        var verticalRatio = lowResolutionHeight / textureHeight;

        return new ProgressiveBlurRenderLayout(
            lowResolutionWidth,
            lowResolutionHeight,
            width / lowResolutionWidth,
            textureHeight / lowResolutionHeight,
            Math.Clamp(blurLength * verticalRatio, 0d, lowResolutionHeight),
            Math.Max(0d, maximumRadius * horizontalRatio),
            Math.Max(0d, maximumRadius * verticalRatio),
            textureHeight,
            seamY,
            seamY);
    }

    private static double AlignToDevicePixel(double value, double dpiScale)
    {
        return Math.Round(value * dpiScale, MidpointRounding.AwayFromZero) / dpiScale;
    }

    private static double CalculateLowResolutionDimension(double fullSize, double dpiScale, double renderScale)
    {
        var lowResolutionPixels = Math.Max(
            1d,
            Math.Round(fullSize * dpiScale * renderScale, MidpointRounding.AwayFromZero));
        return Math.Min(fullSize, lowResolutionPixels / dpiScale);
    }
}
