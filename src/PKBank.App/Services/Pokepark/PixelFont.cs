using Avalonia.Platform;
using SkiaSharp;

namespace PKBank.App.Services.Pokepark;

/// <summary>
/// The bundled NDS12 pixel face for the Skia painters of the park. The font lives in the
/// Avalonia resources (Assets/Fonts/NDS12.ttf), so it is read from the package once and the
/// face is cached for the life of the app.
/// </summary>
public static class PixelFont
{
    private static readonly object Gate = new();
    private static SKTypeface? _face;
    private static Task? _loadTask;
    private static SKTypeface? _fallback;
    private static Task? _fallbackTask;

    public static SKTypeface Face
    {
        get
        {
            var face = Volatile.Read(ref _face);
            if (face is not null) return face;
            _ = WarmAsync();
            // Font loading must never block a paint callback: the next repaint picks it up.
            return SKTypeface.Default;
        }
    }

    public static Task WarmAsync()
    {
        lock (Gate)
            return _loadTask ??= Task.Run(() => Volatile.Write(ref _face, Load("Assets/Fonts/NDS12.ttf")));
    }

    /// <summary>The bundled M PLUS Rounded face: full Latin coverage, the fallback for text
    /// the pixel face cannot draw.</summary>
    public static SKTypeface FallbackFace
    {
        get
        {
            var face = Volatile.Read(ref _fallback);
            if (face is not null) return face;
            _ = WarmFallbackAsync();
            return SKTypeface.Default;
        }
    }

    public static Task WarmFallbackAsync()
    {
        lock (Gate)
            return _fallbackTask ??= Task.Run(() => Volatile.Write(ref _fallback, Load("Assets/Fonts/MPLUSRounded1c-Regular.ttf")));
    }

    private static SKTypeface Load(string logicalPath)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://PKBank/" + logicalPath));
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            return SKTypeface.FromData(SKData.CreateCopy(bytes.ToArray())) ?? SKTypeface.Default;
        }
        catch (Exception)
        {
            return SKTypeface.Default;
        }
    }

    /// <summary>The pixel face at a size (it is a pixel font: crisp edges).</summary>
    public static SKFont At(float size) => new(Face, size) { Edging = SKFontEdging.Antialias, Embolden = true };

    /// <summary>The pixel face when it can draw every glyph of the text, else the system default
    /// (nicknames in scripts the pixel face does not cover).</summary>
    public static SKFont For(string text, float size)
    {
        var face = Face;
        var covered = text.All(c => face.GetGlyph(c) != 0);
        return new SKFont(covered ? face : SKTypeface.Default, size) { Edging = SKFontEdging.Antialias, Embolden = true };
    }
}
