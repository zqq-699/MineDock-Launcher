using System.Windows.Media.Effects;

namespace Launcher.App.Effects;

internal static class ProgressiveGaussianBlurShader
{
    internal const string PackUri =
        "pack://application:,,,/MineDock_Launcher_x64;component/Effects/Shaders/ProgressiveGaussianBlur.ps";

    private static readonly object SyncRoot = new();
    private static PixelShader? pixelShader;
    private static Exception? initializationException;
    private static bool initializationAttempted;

    internal static bool TryGet(out PixelShader? shader, out Exception? exception)
    {
        lock (SyncRoot)
        {
            if (!initializationAttempted)
                Initialize();

            shader = pixelShader;
            exception = initializationException;
            return shader is not null;
        }
    }

    private static void Initialize()
    {
        initializationAttempted = true;
        try
        {
            var loadedShader = new PixelShader
            {
                ShaderRenderMode = ShaderRenderMode.HardwareOnly,
                UriSource = new Uri(PackUri, UriKind.Absolute)
            };
            loadedShader.Freeze();
            pixelShader = loadedShader;
        }
        catch (Exception exception)
        {
            initializationException = exception;
        }
    }
}
