using System.Runtime.InteropServices;
using PKForge.Domain;
using SkiaSharp;

namespace PKForge.Chrome;

/// <summary>A park map's art ready for depth sorting: the ground, the objects' soft shadows
/// painted flat on it, and the solid objects cut into pieces by depth.</summary>
public sealed class ParkArt
{
    public sealed record Piece(SKBitmap Bitmap, int Left, int Top, int Base);

    public required SKBitmap Ground { get; init; }
    public required SKBitmap Shadows { get; init; }
    public required IReadOnlyList<Piece> Pieces { get; init; }

    private (SKColor Light, SKBitmap Ground, Piece[] Pieces)? _lit;

    /// <summary>The art in the day's light, drawn once per light: the ground with its shadows, and
    /// every piece, already tinted, so a frame only copies pixels.</summary>
    public (SKBitmap Ground, IReadOnlyList<Piece> Pieces) LitBy(SKColor light)
    {
        if (_lit is { } cached && cached.Light == light) return (cached.Ground, cached.Pieces);
        using var filter = light == SKColors.White ? null : SKColorFilter.CreateBlendMode(light, SKBlendMode.Modulate);
        using var paint = new SKPaint { IsAntialias = false, ColorFilter = filter };
        SKBitmap Tint(SKBitmap source, SKBitmap? over = null)
        {
            var bitmap = new SKBitmap(new SKImageInfo(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, 0, 0, paint);
            if (over is not null) canvas.DrawBitmap(over, 0, 0, paint);
            return bitmap;
        }
        var ground = Tint(Ground, Shadows);
        var pieces = Pieces.Select(p => p with { Bitmap = Tint(p.Bitmap) }).ToArray();
        if (_lit is { } old)
        {
            old.Ground.Dispose();
            foreach (var p in old.Pieces) p.Bitmap.Dispose();
        }
        _lit = (light, ground, pieces);
        return (ground, pieces);
    }

    /// <summary>Takes ownership of the layers, which must have the same size. depth holds every
    /// solid pixel's base (red: high byte, green: low byte); without it each touching group of
    /// solid pixels is one piece.</summary>
    public static ParkArt Cut(SKBitmap ground, SKBitmap objects, SKBitmap? depth = null)
    {
        if (ground.Width != objects.Width || ground.Height != objects.Height)
            throw new ArgumentException("The ground and objects layers differ in size.", nameof(objects));
        if (depth is not null && (depth.Width != ground.Width || depth.Height != ground.Height))
            throw new ArgumentException("The depth map differs in size from the ground.", nameof(depth));
        var (source, stride, alphaType) = Rgba(objects);
        int width = objects.Width, height = objects.Height;
        objects.Dispose();
        var alpha = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                alpha[y * width + x] = source[y * stride + x * 4 + 3];
        int[]? bases = null;
        if (depth is not null)
        {
            var (d, dStride, _) = Rgba(depth);
            depth.Dispose();
            bases = new int[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                    bases[y * width + x] = d[y * dStride + x * 4] << 8 | d[y * dStride + x * 4 + 1];
        }

        var pieces = ParkLayerCut.Split(alpha, width, height, bases).Select(piece =>
        {
            var pixels = new byte[piece.Width * piece.Height * 4];
            foreach (var i in piece.Pixels)
            {
                int x = i % width, y = i / width;
                Array.Copy(source, y * stride + x * 4, pixels, ((y - piece.Top) * piece.Width + x - piece.Left) * 4, 4);
            }
            return new Piece(Bitmap(piece.Width, piece.Height, alphaType, pixels), piece.Left, piece.Top, piece.Base);
        }).ToArray();

        var shadows = new byte[width * height * 4];
        for (var i = 0; i < alpha.Length; i++)
            if (alpha[i] is > 0 and < 255)
                Array.Copy(source, i / width * stride + i % width * 4, shadows, i * 4, 4);
        return new() { Ground = ground, Shadows = Bitmap(width, height, alphaType, shadows), Pieces = pieces };
    }

    // The pixels as bytes, four per pixel with alpha last (straight RGBA for depth, whatever the decoder chose).
    private static (byte[] Bytes, int Stride, SKAlphaType AlphaType) Rgba(SKBitmap bitmap)
    {
        using var copy = bitmap.ColorType == SKColorType.Rgba8888 ? null : bitmap.Copy(SKColorType.Rgba8888);
        var rgba = copy ?? bitmap;
        return (rgba.GetPixelSpan().ToArray(), rgba.RowBytes, rgba.AlphaType);
    }

    private static SKBitmap Bitmap(int width, int height, SKAlphaType alphaType, byte[] pixels)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, alphaType));
        var target = bitmap.GetPixels();
        for (var y = 0; y < height; y++)
            Marshal.Copy(pixels, y * width * 4, target + y * bitmap.RowBytes, width * 4);
        bitmap.NotifyPixelsChanged();
        return bitmap;
    }
}

/// <summary>A frozen camera: the scale and the top-left corner in art pixels (see <see cref="ParkViewport"/>).</summary>
public readonly record struct ParkCamera(float Scale, float Left, float Top)
{
    /// <summary>The part of the map in a view of that size, in art pixels.</summary>
    public SKRect Visible(int width, int height) => SKRect.Create(Left, Top, width / (float)Scale + 1, height / (float)Scale + 1);

    public static ParkCamera Of(ParkViewport viewport)
    {
        var (left, top) = viewport.Corner;
        return new(viewport.Scale, left, top);
    }

    /// <summary>The scale that fills the view, looking at one point.</summary>
    public static ParkCamera Fit(ParkMap map, int width, int height, SKPoint center)
    {
        var viewport = new ParkViewport(map.Width, map.Height);
        viewport.Resize(width, height);
        viewport.Follow(center.X, center.Y);
        return Of(viewport);
    }

    /// <summary>From here on the canvas draws in art pixels.</summary>
    public void Apply(SKCanvas canvas, ParkMap map)
    {
        canvas.Scale(Scale);
        canvas.Translate(-Left, -Top);
        canvas.ClipRect(SKRect.Create(0, 0, map.Width, map.Height));
    }

    public SKPoint ToMap(float screenX, float screenY) => new(Left + screenX / Scale, Top + screenY / Scale);
}

public static class ParkPaint
{
    /// <summary>The lit ground, only the part in view.</summary>
    public static void Ground(SKCanvas canvas, SKBitmap litGround, SKRect visible, SKPaint paint)
    {
        var part = SKRect.Intersect(visible, SKRect.Create(0, 0, litGround.Width, litGround.Height));
        if (part.IsEmpty) return;
        paint.Color = SKColors.White;
        canvas.DrawBitmap(litGround, part, part, paint);
    }

    /// <summary>Objects and residents share one depth order: whoever stands lower on the map is
    /// in front, so a Pokémon walks behind the statue's plinth and under the arch's beam.
    /// A resident standing on an object's base line is drawn in front of it. Pieces out of view
    /// are skipped.</summary>
    public static void InDepthOrder(SKCanvas canvas, IReadOnlyList<ParkArt.Piece> pieces, SKRect visible, SKPaint paint,
        IReadOnlyList<(float Feet, Action Draw)> residents)
    {
        var layers = new List<(float Depth, int Order, Action Draw)>(residents.Count + 64);
        foreach (var piece in pieces)
        {
            if (piece.Left > visible.Right || piece.Top > visible.Bottom || piece.Left + piece.Bitmap.Width < visible.Left ||
                piece.Top + piece.Bitmap.Height < visible.Top) continue;
            layers.Add((piece.Base, 0, () => { paint.Color = SKColors.White; canvas.DrawBitmap(piece.Bitmap, piece.Left, piece.Top, paint); }));
        }
        foreach (var (feet, draw) in residents) layers.Add((feet, 1, draw));
        layers.Sort((a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Order.CompareTo(b.Order));
        foreach (var layer in layers) layer.Draw();
    }
}

/// <summary>
/// The park drawn at its own resolution: every sprite and every piece of the map sits on whole
/// art pixels, so the world is painted into a small bitmap one art pixel per pixel (about 320×180
/// at the default zoom, 36 times fewer pixels than the screen) and scaled up to the screen in one
/// draw, nearest-neighbour. The bitmap is one art pixel bigger all round and shifted by the
/// camera's fraction of an art pixel, so scrolling stays as smooth as before. Reused frame to
/// frame; one per view (the park, the lower screen).
/// </summary>
public sealed class ParkLowRes : IDisposable
{
    private SKBitmap? _bitmap;
    private SKCanvas? _canvas;
    private float _left, _top;
    private SKRectI _used;
    private readonly SKPaint _blit = new() { IsAntialias = false };

    /// <summary>A canvas in art pixels for this frame, cleared, clipped to the map.</summary>
    public SKCanvas Begin(ParkCamera camera, int mapWidth, int mapHeight, int width, int height, SKColor background)
    {
        int w = (int)MathF.Ceiling(width / camera.Scale) + 2, h = (int)MathF.Ceiling(height / camera.Scale) + 2;
        // Kept while big enough (a zoom glides through many sizes): only grown, in steps of 64.
        if (_bitmap is null || _bitmap.Width < w || _bitmap.Height < h || _bitmap.Width > w * 3)
        {
            _canvas?.Dispose(); _bitmap?.Dispose();
            _bitmap = new SKBitmap((w + 63) / 64 * 64, (h + 63) / 64 * 64);
            _canvas = new SKCanvas(_bitmap);
        }
        _used = new SKRectI(0, 0, w, h);
        _left = MathF.Floor(camera.Left); _top = MathF.Floor(camera.Top);
        _canvas!.Clear(background);
        _canvas.Save();
        _canvas.ClipRect(SKRect.Create(0, 0, w, h));
        _canvas.Translate(-_left, -_top);
        _canvas.ClipRect(SKRect.Create(0, 0, mapWidth, mapHeight));
        return _canvas;
    }

    /// <summary>The frame onto the screen, scaled up.</summary>
    public void End(SKCanvas screen, ParkCamera camera)
    {
        if (_bitmap is null || _canvas is null) return;
        _canvas.Restore();
        var dest = SKRect.Create((_left - camera.Left) * camera.Scale, (_top - camera.Top) * camera.Scale,
            _used.Width * camera.Scale, _used.Height * camera.Scale);
        screen.DrawBitmap(_bitmap, _used, dest, _blit);
    }

    public void Dispose()
    {
        _canvas?.Dispose(); _bitmap?.Dispose(); _blit.Dispose();
    }
}
