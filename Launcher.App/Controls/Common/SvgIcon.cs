using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Launcher.App.Controls;

public sealed class SvgIcon : Control
{
    static SvgIcon()
    {
        ForegroundProperty.OverrideMetadata(
            typeof(SvgIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    }

    public static readonly DependencyProperty IconKeyProperty =
        DependencyProperty.Register(
            nameof(IconKey),
            typeof(string),
            typeof(SvgIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty =
        DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(SvgIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Dictionary<string, SvgIconData?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    public SvgIcon()
    {
        SetResourceReference(ForegroundProperty, "Brush.Icon.Primary");
        SetResourceReference(StrokeProperty, "Brush.Icon.Primary");
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (string.IsNullOrWhiteSpace(IconKey) || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        try
        {
            var icon = GetIconData(IconKey);
            if (icon is null || icon.ViewBox.Width <= 0 || icon.ViewBox.Height <= 0)
                return;

            var scale = Math.Min(ActualWidth / icon.ViewBox.Width, ActualHeight / icon.ViewBox.Height);
            var scaledWidth = icon.ViewBox.Width * scale;
            var scaledHeight = icon.ViewBox.Height * scale;
            var offsetX = (ActualWidth - scaledWidth) / 2 - icon.ViewBox.X * scale;
            var offsetY = (ActualHeight - scaledHeight) / 2 - icon.ViewBox.Y * scale;

            var pushedTransforms = 0;
            try
            {
                drawingContext.PushTransform(new TranslateTransform(offsetX, offsetY));
                pushedTransforms++;
                drawingContext.PushTransform(new ScaleTransform(scale, scale));
                pushedTransforms++;

                var fillBrush = Foreground;
                var strokeBrush = Stroke ?? fillBrush;
                foreach (var shape in icon.Shapes)
                {
                    var pen = shape.HasStroke && strokeBrush is not null
                        ? new Pen(strokeBrush, shape.StrokeThickness)
                        {
                            StartLineCap = shape.LineCap,
                            EndLineCap = shape.LineCap,
                            LineJoin = shape.LineJoin
                        }
                        : null;
                    drawingContext.DrawGeometry(shape.HasFill ? fillBrush : null, pen, shape.Geometry);
                }
            }
            finally
            {
                while (pushedTransforms > 0)
                {
                    drawingContext.Pop();
                    pushedTransforms--;
                }
            }
        }
        catch
        {
            // Broken or unsupported SVG files must not prevent the launcher window from opening.
        }
    }

    private static SvgIconData? GetIconData(string iconKey)
    {
        if (IconCache.TryGetValue(iconKey, out var cached))
            return cached;

        var icon = LoadIconData(iconKey);
        IconCache[iconKey] = icon;
        return icon;
    }

    private static SvgIconData? LoadIconData(string iconKey)
    {
        try
        {
            var safeIconKey = iconKey.Replace("\\", "/", StringComparison.Ordinal).TrimStart('/');
            var uri = new Uri($"/Assets/Icons/{safeIconKey}.svg", UriKind.Relative);
            var resource = System.Windows.Application.GetResourceStream(uri);
            if (resource is null)
                return null;

            using var stream = resource.Stream;
            var document = XDocument.Load(stream);
            var root = document.Root;
            if (root is null)
                return null;

            var viewBox = ParseViewBox(root.Attribute("viewBox")?.Value);
            var shapes = new List<SvgShape>();
            ReadShapes(root, shapes);
            return new SvgIconData(viewBox, shapes);
        }
        catch
        {
            return null;
        }
    }

    private static void ReadShapes(XElement element, List<SvgShape> shapes, SvgStyleContext? inheritedStyle = null)
    {
        var currentStyle = SvgStyleContext.Merge(inheritedStyle, element);

        foreach (var child in element.Elements())
        {
            var name = child.Name.LocalName;
            if (name is "defs" or "clipPath" or "title" or "desc")
                continue;

            if (name == "path")
            {
                var data = child.Attribute("d")?.Value;
                if (!string.IsNullOrWhiteSpace(data))
                    shapes.Add(CreateShape(child, Geometry.Parse(data), currentStyle));
                continue;
            }

            if (name == "circle")
            {
                var center = new Point(ParseDouble(child.Attribute("cx")?.Value), ParseDouble(child.Attribute("cy")?.Value));
                var radius = ParseDouble(child.Attribute("r")?.Value);
                shapes.Add(CreateShape(child, new EllipseGeometry(center, radius, radius), currentStyle));
                continue;
            }

            if (name == "rect")
            {
                var x = ParseDouble(child.Attribute("x")?.Value);
                var y = ParseDouble(child.Attribute("y")?.Value);
                var width = ParseDouble(child.Attribute("width")?.Value);
                var height = ParseDouble(child.Attribute("height")?.Value);
                var radiusX = ParseDouble(child.Attribute("rx")?.Value);
                var radiusY = ParseDouble(child.Attribute("ry")?.Value);
                shapes.Add(CreateShape(child, new RectangleGeometry(new Rect(x, y, width, height), radiusX, radiusY), currentStyle));
                continue;
            }

            ReadShapes(child, shapes, currentStyle);
        }
    }

    private static SvgShape CreateShape(XElement element, Geometry geometry, SvgStyleContext inheritedStyle)
    {
        var transform = ParseTransform(element.Attribute("transform")?.Value);
        if (transform is not null)
            geometry.Transform = transform;

        var style = SvgStyleContext.Merge(inheritedStyle, element);
        geometry.Freeze();
        return new SvgShape(
            geometry,
            HasPaint(style.Fill),
            HasPaint(style.Stroke),
            ParseDouble(style.StrokeWidth, 1),
            ParseLineCap(style.StrokeLineCap),
            ParseLineJoin(style.StrokeLineJoin));
    }

    private static Rect ParseViewBox(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new Rect(0, 0, 24, 24);

        var parts = value
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();

        return parts.Length == 4
            ? new Rect(parts[0], parts[1], parts[2], parts[3])
            : new Rect(0, 0, 24, 24);
    }

    private static bool HasPaint(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static double ParseDouble(string? value, double fallback = 0)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : fallback;
    }

    private static PenLineCap ParseLineCap(string? value)
    {
        return string.Equals(value, "round", StringComparison.OrdinalIgnoreCase)
            ? PenLineCap.Round
            : PenLineCap.Flat;
    }

    private static PenLineJoin ParseLineJoin(string? value)
    {
        return string.Equals(value, "round", StringComparison.OrdinalIgnoreCase)
            ? PenLineJoin.Round
            : PenLineJoin.Miter;
    }

    private static Transform? ParseTransform(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var matrix = Matrix.Identity;
        foreach (Match match in Regex.Matches(value, @"([a-zA-Z]+)\(([^)]*)\)"))
        {
            var name = match.Groups[1].Value;
            var values = match.Groups[2].Value
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
                .ToArray();

            if (name.Equals("matrix", StringComparison.OrdinalIgnoreCase) && values.Length == 6)
            {
                matrix.Append(new Matrix(values[0], values[1], values[2], values[3], values[4], values[5]));
                continue;
            }

            if (name.Equals("translate", StringComparison.OrdinalIgnoreCase) && values.Length >= 1)
            {
                matrix.Translate(values[0], values.Length > 1 ? values[1] : 0);
                continue;
            }

            if (name.Equals("scale", StringComparison.OrdinalIgnoreCase) && values.Length >= 1)
            {
                matrix.Scale(values[0], values.Length > 1 ? values[1] : values[0]);
            }
        }

        if (matrix.IsIdentity)
            return null;

        var transform = new MatrixTransform(matrix);
        transform.Freeze();
        return transform;
    }

    private sealed record SvgIconData(Rect ViewBox, IReadOnlyList<SvgShape> Shapes);

    private sealed record SvgShape(
        Geometry Geometry,
        bool HasFill,
        bool HasStroke,
        double StrokeThickness,
        PenLineCap LineCap,
        PenLineJoin LineJoin);

    private sealed record SvgStyleContext(
        string? Fill,
        string? Stroke,
        string? StrokeWidth,
        string? StrokeLineCap,
        string? StrokeLineJoin)
    {
        public static SvgStyleContext Merge(SvgStyleContext? inheritedStyle, XElement element)
        {
            return new SvgStyleContext(
                element.Attribute("fill")?.Value ?? inheritedStyle?.Fill,
                element.Attribute("stroke")?.Value ?? inheritedStyle?.Stroke,
                element.Attribute("stroke-width")?.Value ?? inheritedStyle?.StrokeWidth,
                element.Attribute("stroke-linecap")?.Value ?? inheritedStyle?.StrokeLineCap,
                element.Attribute("stroke-linejoin")?.Value ?? inheritedStyle?.StrokeLineJoin);
        }
    }
}
