using SkiaSharp;

namespace PKBank.App.Theme;

/// <summary>
/// One-off Skia bakes of the shared chrome (the logo grid backdrop) so the Avalonia side
/// never needs a Skia canvas: the baked images are cached as long as the window lives.
/// </summary>
public static class BackdropBake
{
    public static MemoryStream BakeLogoGrid(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        PKForge.Chrome.PksmPaint.LogoGrid(canvas, new SKRect(0, 0, width, height));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new MemoryStream(data.ToArray());
    }
}
