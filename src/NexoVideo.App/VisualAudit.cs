using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NexoVideo.App;

/// <summary>Evidência do modo --render-ui: arquivos realmente carregados e geometria renderizada.</summary>
internal static class VisualAudit
{
    public static void Write(Window window, FrameworkElement root, string target)
    {
        var images = new List<object>();
        void Visit(DependencyObject element)
        {
            if (element is Image { Source: { } image } control)
            {
                var position = control.TransformToAncestor(root).Transform(new Point());
                images.Add(new { asset = image.ToString(), width = control.ActualWidth,
                    height = control.ActualHeight, sourceKind = image.GetType().Name, pixelWidth = (image as BitmapSource)?.PixelWidth, pixelHeight = (image as BitmapSource)?.PixelHeight,
                    x = position.X, y = position.Y });
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i));
        }
        Visit(root);
        var typeface = new Typeface(window.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var fontLoaded = typeface.TryGetGlyphTypeface(out var glyph);
        File.WriteAllText(target, JsonSerializer.Serialize(new
        {
            width = root.ActualWidth, height = root.ActualHeight, fontLoaded,
            font = fontLoaded ? glyph.FontUri.ToString() : window.FontFamily.Source, images
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
