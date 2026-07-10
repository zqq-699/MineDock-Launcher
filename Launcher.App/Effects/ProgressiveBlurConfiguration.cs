namespace Launcher.App.Effects;

internal static class ProgressiveBlurResourceKeys
{
    internal const string IsEnabled = "Is.ProgressiveBlur.Enabled";
    internal const string MaximumRadius = "ListPage.ProgressiveBlur.MaxRadius";
    internal const string RenderScale = "ListPage.ProgressiveBlur.RenderScale";
    internal const string ActiveMinimumOpacity = "ListPage.ProgressiveBlur.ActiveMinimumOpacity";
    internal const string ActiveIntermediateOpacity = "ListPage.ProgressiveBlur.ActiveIntermediateOpacity";
}

internal static class ProgressiveBlurDefaults
{
    internal const double MaximumRadius = 24d;
    internal const double RenderScale = 0.4d;
    internal const double ActiveMinimumOpacity = 0d;
    internal const double ActiveIntermediateOpacity = 0.4d;
    internal const double SamplingGuardLength = 24d;
    internal const double TextureOverscanLength = 4d;
    internal const double MinimumRenderScale = 0.1d;
}
