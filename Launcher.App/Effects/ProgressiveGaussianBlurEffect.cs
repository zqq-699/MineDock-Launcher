using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Launcher.App.Effects;

internal sealed class ProgressiveGaussianBlurEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty(
            nameof(Input),
            typeof(ProgressiveGaussianBlurEffect),
            0,
            SamplingMode.Bilinear);

    public static readonly DependencyProperty InputWidthProperty = RegisterConstantProperty(nameof(InputWidth), 1d, 0, IsPositiveFinite);
    public static readonly DependencyProperty InputHeightProperty = RegisterConstantProperty(nameof(InputHeight), 1d, 1, IsPositiveFinite);
    public static readonly DependencyProperty BlurLengthProperty = RegisterConstantProperty(nameof(BlurLength), 0d, 2, IsNonNegativeFinite);
    public static readonly DependencyProperty MaximumRadiusProperty = RegisterConstantProperty(nameof(MaximumRadius), 24d, 3, IsNonNegativeFinite);
    public static readonly DependencyProperty DirectionXProperty = RegisterConstantProperty(nameof(DirectionX), 1d, 4, IsFinite);
    public static readonly DependencyProperty DirectionYProperty = RegisterConstantProperty(nameof(DirectionY), 0d, 5, IsFinite);

    private ProgressiveGaussianBlurEffect(PixelShader pixelShader)
    {
        PixelShader = pixelShader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(InputWidthProperty);
        UpdateShaderValue(InputHeightProperty);
        UpdateShaderValue(BlurLengthProperty);
        UpdateShaderValue(MaximumRadiusProperty);
        UpdateShaderValue(DirectionXProperty);
        UpdateShaderValue(DirectionYProperty);
    }

    public Brush? Input
    {
        get => (Brush?)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double InputWidth
    {
        get => (double)GetValue(InputWidthProperty);
        set => SetValue(InputWidthProperty, value);
    }

    public double InputHeight
    {
        get => (double)GetValue(InputHeightProperty);
        set => SetValue(InputHeightProperty, value);
    }

    public double BlurLength
    {
        get => (double)GetValue(BlurLengthProperty);
        set => SetValue(BlurLengthProperty, value);
    }

    public double MaximumRadius
    {
        get => (double)GetValue(MaximumRadiusProperty);
        set => SetValue(MaximumRadiusProperty, value);
    }

    public double DirectionX
    {
        get => (double)GetValue(DirectionXProperty);
        set => SetValue(DirectionXProperty, value);
    }

    public double DirectionY
    {
        get => (double)GetValue(DirectionYProperty);
        set => SetValue(DirectionYProperty, value);
    }

    internal static bool TryCreate(
        double directionX,
        double directionY,
        out ProgressiveGaussianBlurEffect? effect,
        out Exception? exception)
    {
        if (!ProgressiveGaussianBlurShader.TryGet(out var shader, out exception) || shader is null)
        {
            effect = null;
            return false;
        }

        effect = new ProgressiveGaussianBlurEffect(shader)
        {
            DirectionX = directionX,
            DirectionY = directionY
        };
        return true;
    }

    private static DependencyProperty RegisterConstantProperty(
        string name,
        double defaultValue,
        int registerIndex,
        ValidateValueCallback validateValueCallback)
    {
        return DependencyProperty.Register(
            name,
            typeof(double),
            typeof(ProgressiveGaussianBlurEffect),
            new UIPropertyMetadata(defaultValue, PixelShaderConstantCallback(registerIndex)),
            validateValueCallback);
    }

    private static bool IsFinite(object value) => double.IsFinite((double)value);

    private static bool IsPositiveFinite(object value)
    {
        var number = (double)value;
        return double.IsFinite(number) && number > 0d;
    }

    private static bool IsNonNegativeFinite(object value)
    {
        var number = (double)value;
        return double.IsFinite(number) && number >= 0d;
    }
}
