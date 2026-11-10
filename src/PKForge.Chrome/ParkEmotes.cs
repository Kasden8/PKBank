using PKForge.Domain;
using SkiaSharp;

namespace PKForge.Chrome;

/// <summary>The little icons over a Pokémon's head, drawn in art pixels: most sit in a speech
/// bubble; the sweat drop and the sleeping Z float on their own.</summary>
public static class ParkEmotes
{
    private static readonly SKColor Outline = new(0x2b, 0x2d, 0x42), Paper = new(0xff, 0xfb, 0xec);

    private static readonly Dictionary<ParkEmote, (string[] Glyph, SKColor Color)> Glyphs = new()
    {
        [ParkEmote.Heart] = ([".##.##.", "#######", "#######", ".#####.", "..###..", "...#..."], new(0xe0, 0x4f, 0x6a)),
        [ParkEmote.Note] = (["...##..", "...#.#.", "...#..#", "...#...", ".###...", ".###..."], new(0x3d, 0x6f, 0xc9)),
        [ParkEmote.Exclaim] = (["...#...", "...#...", "...#...", "...#...", ".......", "...#..."], new(0xd9, 0x3a, 0x3a)),
        [ParkEmote.Question] = (["..###..", ".#...#.", "....#..", "...#...", ".......", "...#..."], new(0x3d, 0x6f, 0xc9)),
        [ParkEmote.Anger] = (["##...##", ".#...#.", ".......", ".#...#.", "##...##"], new(0xd9, 0x3a, 0x3a)),
        [ParkEmote.Sparkle] = (["...#...", "..###..", "#######", "..###..", ".##.##.", "##...##"], new(0xe8, 0xb0, 0x20)),
        [ParkEmote.Ellipsis] = ([".......", ".......", "#..#..#"], new(0x55, 0x58, 0x6e)),
    };

    private static readonly string[] Drop = ["..#", ".##", "###", "###", ".#."];
    private static readonly string[] Zed = ["####", "..#.", ".#..", "####"];

    /// <summary>x, y: just above the head. age: ms since the emote started, for the pop-in and float.</summary>
    public static void Draw(SKCanvas canvas, SKPaint paint, ParkEmote emote, float x, float y, long age)
    {
        x = MathF.Round(x); y = MathF.Round(y);
        switch (emote)
        {
            case ParkEmote.None:
                return;
            case ParkEmote.Sleep:
                // Two Zs rise and fade in turn: pale with a dark edge, readable on grass or wood.
                for (var i = 0; i < 2; i++)
                {
                    var t = (age / 1400f + i * .5f) % 1;
                    var alpha = (byte)(255 * Math.Min(1, 2 * (1 - t)));
                    float zx = x + 2 + i * 3 + MathF.Round(t * 3), zy = y - 4 - MathF.Round(t * 8);
                    paint.Color = Outline.WithAlpha(alpha);
                    Glyph(canvas, paint, Zed, zx + 1, zy + 1);
                    paint.Color = new SKColor(0xe9, 0xf1, 0xff, alpha);
                    Glyph(canvas, paint, Zed, zx, zy);
                }
                return;
            case ParkEmote.Sweat:
                paint.Color = new SKColor(0x5d, 0xa9, 0xe9);
                Glyph(canvas, paint, Drop, x + 1, y - 6 + MathF.Round(Math.Min(age, 600) / 300f));
                return;
        }
        if (!Glyphs.TryGetValue(emote, out var icon)) return;
        // A quick pop: the bubble rises two pixels as it appears.
        var lift = age < 120 ? 2 : age < 240 ? 1 : 0;
        float left = x - 5, top = y - 11 + lift;
        paint.Color = Outline;
        canvas.DrawRect(left + 1, top, 9, 9, paint);
        canvas.DrawRect(left, top + 1, 11, 7, paint);
        canvas.DrawRect(left + 2, top + 9, 2, 1, paint);   // tail
        paint.Color = Paper;
        canvas.DrawRect(left + 1, top + 1, 9, 7, paint);
        paint.Color = icon.Color;
        Glyph(canvas, paint, icon.Glyph, left + 2, top + 1 + (7 - icon.Glyph.Length) / 2);
    }

    /// <summary>What a sleeper dreams of, shown in a little cloud: a berry, a heart, a ball, a star.</summary>
    public enum Dream { Berry, Heart, Ball, Star }

    private static readonly Dictionary<Dream, (string[] Glyph, SKColor Color)> Dreams = new()
    {
        [Dream.Berry] = ([".##.", "####", "####", ".##."], new(0x3f, 0x7c, 0xd8)),
        [Dream.Heart] = (["#.#", "###", ".#."], new(0xe0, 0x4f, 0x6a)),
        [Dream.Ball] = ([".##.", "#rr#", "#ww#", ".##."], new(0xe8, 0x5a, 0x4f)),
        [Dream.Star] = ([".#.", "###", ".#."], new(0xe8, 0xb0, 0x20)),
    };

    public static void DrawDream(SKCanvas canvas, SKPaint paint, Dream dream, float x, float y, long age)
    {
        x = MathF.Round(x); y = MathF.Round(y - MathF.Min(2, age / 200f));
        // Two small puffs lead up to the cloud.
        paint.Color = Paper.WithAlpha(220);
        canvas.DrawRect(x - 1, y - 2, 1, 1, paint);
        canvas.DrawRect(x + 1, y - 5, 2, 2, paint);
        canvas.DrawRect(x + 2, y - 13, 9, 7, paint);
        canvas.DrawRect(x + 3, y - 14, 7, 9, paint);
        var (glyph, color) = Dreams[dream];
        for (var j = 0; j < glyph.Length; j++)
            for (var i = 0; i < glyph[j].Length; i++)
            {
                if (glyph[j][i] == '.') continue;
                paint.Color = glyph[j][i] == 'w' ? new SKColor(0xfa, 0xf6, 0xea) : color;
                canvas.DrawRect(x + 6 - glyph[j].Length / 2 + i, y - 11 + j, 1, 1, paint);
            }
    }

    private static void Glyph(SKCanvas canvas, SKPaint paint, string[] rows, float left, float top)
    {
        for (var y = 0; y < rows.Length; y++)
            for (var x = 0; x < rows[y].Length; x++)
                if (rows[y][x] == '#') canvas.DrawRect(left + x, top + y, 1, 1, paint);
    }
}
