using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace GlassDock;

internal static class BatteryIcons
{
    private static readonly Dictionary<(string Code, Color Outline), DrawingImage> Images = new();

    internal static DrawingImage Get(byte percent, bool charging, Color outline)
    {
        var code = percent switch
        {
            255 => charging ? "E85F" : "E855",
            < 35 => charging ? "E85C" : "E852",
            < 65 => charging ? "E85F" : "E855",
            < 90 => charging ? "E862" : "E858",
            _ => charging ? "EA93" : "E83F"
        };
        if (Images.TryGetValue((code, outline), out var existing)) return existing;

        var uri = new Uri($"pack://application:,,,/Assets/Battery/{code}.svg");
        using var stream = Application.GetResourceStream(uri).Stream;
        var svg = XDocument.Load(stream).Root!;
        var drawing = new DrawingGroup();
        // SysBatt uses a 30 by 24 advance box with the baseline at Y=0.
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 30, 24))));
        var glyph = new DrawingGroup { Transform = new TranslateTransform(0, 24) };
        foreach (var path in svg.Elements(svg.Name.Namespace + "path"))
        {
            var color = (string)path.Attribute("fill")!;
            var brush = color == "currentColor" ? new SolidColorBrush(outline) : (Brush)new BrushConverter().ConvertFromInvariantString(color)!;
            var fillRule = (string?)path.Attribute("fill-rule") == "nonzero" ? "F1 " : "F0 ";
            var geometry = Geometry.Parse(fillRule + (string)path.Attribute("d")!);
            glyph.Children.Add(new GeometryDrawing(brush, null, geometry));
        }
        drawing.Children.Add(glyph);

        var image = new DrawingImage(drawing);
        image.Freeze();
        Images.Add((code, outline), image);
        return image;
    }
}
