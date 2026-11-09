using SkiaSharp;

namespace PKForge.Chrome;

/// <summary>Pokémon in the water. A swimmer shows whole above the surface and, just under it, a
/// little of its body through the water, tinted and fading with depth. A giant deep under the
/// sea shows as its own shape, dark and flattened by the water above it, swaying, with a bubble
/// rising now and then.</summary>
public static class ParkWater
{
    private static readonly SKColor Shallow = new(24, 70, 150);
    private static readonly SKColor Deeper = new(12, 40, 96);

    /// <param name="dest">Where the sprite goes, on whole art pixels.</param>
    /// <param name="waterline">The surface's row: above it the sprite as it is.</param>
    public static void Swimmer(SKCanvas c, SKPaint p, SKBitmap bitmap, SKRect source, SKRect dest, float waterline)
    {
        c.Save();
        c.ClipRect(new SKRect(-10000, -10000, 100000, waterline));
        c.DrawBitmap(bitmap, source, dest, p);
        c.Restore();
        // Under the surface: two bands, the nearer one clearer.
        var light = p.ColorFilter;
        using var tint = SKColorFilter.CreateBlendMode(Shallow, SKBlendMode.SrcIn);
        using var lit = light is null ? null : SKColorFilter.CreateCompose(light, tint);
        p.ColorFilter = lit ?? tint;
        foreach (var (top, height, alpha) in new[] { (0, 3, 150), (3, 5, 75) })
        {
            c.Save();
            c.ClipRect(new SKRect(-10000, waterline + top, 100000, waterline + top + height));
            p.Color = SKColors.White.WithAlpha((byte)alpha);
            c.DrawBitmap(bitmap, source, dest, p);
            c.Restore();
        }
        p.ColorFilter = light;
        p.Color = SKColors.White;
    }

    /// <param name="feet">Where its feet would be: the shape lies flattened around them.</param>
    public static void Deep(SKCanvas c, SKPaint p, SKBitmap bitmap, SKRect source, SKPoint middle, SKPoint feet, long time)
    {
        var sway = MathF.Round(MathF.Sin(time / 900f) * 1.5f);
        var w = source.Width;
        var h = MathF.Round(source.Height * .55f);
        // Flattened toward its feet, as if seen through deep water from above.
        var top = MathF.Round(feet.Y - (feet.Y - (middle.Y - source.Height / 2f)) * .55f);
        var dest = SKRect.Create(MathF.Round(middle.X - w / 2f + sway), top, w, h);
        var light = p.ColorFilter;
        using var tint = SKColorFilter.CreateBlendMode(Deeper, SKBlendMode.SrcIn);
        using var lit = light is null ? null : SKColorFilter.CreateCompose(light, tint);
        p.ColorFilter = lit ?? tint;
        p.Color = SKColors.White.WithAlpha(105);
        c.DrawBitmap(bitmap, source, dest, p);
        p.ColorFilter = light;
        // A bubble now and then, rising from its head and bursting at the surface.
        for (var i = 0; i < 2; i++)
        {
            var t = (time / 1400f + i * .5f) % 1;
            if (t > .7f) continue;
            var x = MathF.Round(dest.MidX + (i == 0 ? -3 : 4) + MathF.Sin(t * 9) * 1);
            var y = MathF.Round(dest.Top + 2 - t * 8);
            p.Color = new SKColor(0xd8, 0xf0, 0xff, (byte)(200 * (1 - t / .7f)));
            c.DrawRect(x, y, 1, 1, p);
            if (i == 0) { c.DrawRect(x + 1, y + 2, 1, 1, p); }
        }
        p.Color = SKColors.White;
    }
}
