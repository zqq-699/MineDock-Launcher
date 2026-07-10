using System.Windows.Media;
using System.Windows.Media.Effects;
using Launcher.App.Effects;

namespace Launcher.App.Services;

internal enum ProgressiveBlurUnavailableReason
{
    None,
    RenderingTierTooLow,
    PixelShader30Unsupported,
    ShaderLoadFailed,
    ShaderRejected
}

internal readonly record struct ProgressiveBlurCapabilitySnapshot(
    bool IsAvailable,
    int RenderingTier,
    bool IsPixelShader30Supported,
    ProgressiveBlurUnavailableReason UnavailableReason,
    Exception? InitializationException);

internal interface IProgressiveBlurSupport : IDisposable
{
    ProgressiveBlurCapabilitySnapshot Current { get; }

    event EventHandler? AvailabilityChanged;
}

internal static class ProgressiveBlurCapabilityEvaluator
{
    internal const int MinimumRenderingTier = 2;

    internal static ProgressiveBlurCapabilitySnapshot Evaluate(
        int renderingTier,
        bool isPixelShader30Supported,
        bool isShaderLoaded,
        bool isShaderRejected,
        Exception? initializationException = null)
    {
        if (isShaderRejected)
        {
            return new ProgressiveBlurCapabilitySnapshot(
                false,
                renderingTier,
                isPixelShader30Supported,
                ProgressiveBlurUnavailableReason.ShaderRejected,
                null);
        }

        if (renderingTier < MinimumRenderingTier)
        {
            return new ProgressiveBlurCapabilitySnapshot(
                false,
                renderingTier,
                isPixelShader30Supported,
                ProgressiveBlurUnavailableReason.RenderingTierTooLow,
                null);
        }

        if (!isPixelShader30Supported)
        {
            return new ProgressiveBlurCapabilitySnapshot(
                false,
                renderingTier,
                false,
                ProgressiveBlurUnavailableReason.PixelShader30Unsupported,
                null);
        }

        if (!isShaderLoaded)
        {
            return new ProgressiveBlurCapabilitySnapshot(
                false,
                renderingTier,
                true,
                ProgressiveBlurUnavailableReason.ShaderLoadFailed,
                initializationException);
        }

        return new ProgressiveBlurCapabilitySnapshot(
            true,
            renderingTier,
            true,
            ProgressiveBlurUnavailableReason.None,
            null);
    }
}

internal sealed class WpfProgressiveBlurSupport : IProgressiveBlurSupport
{
    private readonly object syncRoot = new();
    private ProgressiveBlurCapabilitySnapshot current;
    private bool shaderRejected;
    private bool isDisposed;

    public WpfProgressiveBlurSupport()
    {
        RenderCapability.TierChanged += RenderCapability_TierChanged;
        PixelShader.InvalidPixelShaderEncountered += PixelShader_InvalidPixelShaderEncountered;
        current = EvaluateCurrent();
    }

    public ProgressiveBlurCapabilitySnapshot Current
    {
        get
        {
            lock (syncRoot)
                return current;
        }
    }

    public event EventHandler? AvailabilityChanged;

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (isDisposed)
                return;

            isDisposed = true;
        }

        RenderCapability.TierChanged -= RenderCapability_TierChanged;
        PixelShader.InvalidPixelShaderEncountered -= PixelShader_InvalidPixelShaderEncountered;
    }

    private ProgressiveBlurCapabilitySnapshot EvaluateCurrent()
    {
        var renderingTier = RenderCapability.Tier >> 16;
        var isPixelShader30Supported = RenderCapability.IsPixelShaderVersionSupported(3, 0);
        bool isRejected;
        lock (syncRoot)
            isRejected = shaderRejected;

        if (isRejected
            || renderingTier < ProgressiveBlurCapabilityEvaluator.MinimumRenderingTier
            || !isPixelShader30Supported)
        {
            return ProgressiveBlurCapabilityEvaluator.Evaluate(
                renderingTier,
                isPixelShader30Supported,
                false,
                isRejected);
        }

        var isShaderLoaded = ProgressiveGaussianBlurShader.TryGet(out _, out var initializationException);
        return ProgressiveBlurCapabilityEvaluator.Evaluate(
            renderingTier,
            isPixelShader30Supported,
            isShaderLoaded,
            false,
            initializationException);
    }

    private void Refresh()
    {
        lock (syncRoot)
        {
            if (isDisposed)
                return;
        }

        var next = EvaluateCurrent();
        lock (syncRoot)
        {
            if (isDisposed || current == next)
                return;

            current = next;
        }

        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RenderCapability_TierChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void PixelShader_InvalidPixelShaderEncountered(object? sender, EventArgs e)
    {
        lock (syncRoot)
        {
            if (isDisposed || shaderRejected)
                return;

            shaderRejected = true;
        }

        Refresh();
    }
}
