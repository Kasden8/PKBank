using PKForge.Domain;
using SkiaSharp;

namespace PKForge.Chrome;

/// <summary>Short pixel-art bursts over the park, in art pixels: sparks, a flame puff, frost,
/// bubbles, wisps, droplets, pollen, hearts, a coin, an egg, psychic rings.</summary>
public static class ParkEffects
{
    public const int Duration = 1400;

    /// <summary>How bright the zap's flash over the whole screen is now, 0 to 1.</summary>
    public static float Flash(IEnumerable<ParkEffect> effects, long time) => effects
        .Where(e => e.Kind == ParkEffectKind.Zap && time - e.At is >= 0 and < 160)
        .Select(e => 1 - (time - e.At) / 160f).DefaultIfEmpty(0).Max();

    public static void Draw(SKCanvas canvas, SKPaint paint, ParkEffect effect, long time)
    {
        var age = time - effect.At;
        if (age < 0 || age > Duration) return;
        var t = age / (float)Duration;
        float x = effect.X, y = effect.Y;
        byte Fade(float start = .6f) => (byte)(255 * Math.Clamp((1 - t) / (1 - start), 0, 1));
        void Dot(float px, float py, SKColor color, float size = 1)
        {
            paint.Color = color;
            canvas.DrawRect(MathF.Round(px), MathF.Round(py), size, size, paint);
        }
        switch (effect.Kind)
        {
            case ParkEffectKind.Zap:
                // Zigzag sparks flickering out from the body.
                for (var i = 0; i < 6; i++)
                {
                    if ((age / 70 + i) % 3 == 0) continue;
                    var angle = i * MathF.PI / 3 + age / 300f;
                    float px = x + MathF.Cos(angle) * 9, py = y + MathF.Sin(angle) * 7;
                    var color = new SKColor(255, 240, 90, Fade());
                    Dot(px, py, color); Dot(px + 1, py + 1, color); Dot(px, py + 2, color); Dot(px + 1, py + 3, color);
                    Dot(px - 1, py - 1, new SKColor(255, 255, 230, Fade()));
                }
                break;
            case ParkEffectKind.Burn:
                for (var i = 0; i < 5; i++)
                {
                    var rise = (t * 14 + i * 3) % 14;
                    float px = x - 6 + i * 3 + MathF.Sin(age / 120f + i) * 1.5f, py = y + 4 - rise;
                    Dot(px, py, new SKColor(255, 150, 40, Fade()), 2);
                    Dot(px, py - 2, new SKColor(255, 220, 90, Fade()));
                }
                break;
            case ParkEffectKind.Frost:
                for (var i = 0; i < 6; i++)
                {
                    var angle = i * MathF.PI / 3;
                    var r = 4 + t * 12;
                    float px = x + MathF.Cos(angle) * r, py = y + MathF.Sin(angle) * r * .7f;
                    var color = new SKColor(210, 240, 255, Fade());
                    Dot(px, py, color); Dot(px - 1, py, color); Dot(px + 1, py, color); Dot(px, py - 1, color); Dot(px, py + 1, color);
                }
                break;
            case ParkEffectKind.Poison:
                for (var i = 0; i < 5; i++)
                {
                    var rise = (t * 16 + i * 4) % 16;
                    float px = x - 6 + i * 3, py = y + 2 - rise;
                    Dot(px, py, new SKColor(170, 90, 200, Fade()), (i % 2) + 1);
                }
                break;
            case ParkEffectKind.Ghost or ParkEffectKind.Scare:
                for (var i = 0; i < 4; i++)
                {
                    var angle = i * MathF.PI / 2 + age / 250f;
                    var r = 6 + t * 10;
                    Dot(x + MathF.Cos(angle) * r, y + MathF.Sin(angle) * r * .6f, new SKColor(150, 110, 210, Fade()), 2);
                }
                break;
            case ParkEffectKind.Splash:
                for (var i = 0; i < 6; i++)
                {
                    var dx = (i - 2.5f) * 3;
                    var h = MathF.Sin(t * MathF.PI) * (8 + i % 3 * 3);
                    Dot(x + dx * (1 + t), y + 8 - h, new SKColor(120, 190, 255, Fade()));
                }
                break;
            case ParkEffectKind.Pollen:
                for (var i = 0; i < 8; i++)
                    Dot(x - 8 + i * 2 + MathF.Sin(age / 200f + i) * 2, y - t * 10 + i % 3 * 2, new SKColor(220, 230, 90, Fade()));
                break;
            case ParkEffectKind.Sting or ParkEffectKind.Bite:
                for (var i = 0; i < 4; i++)
                {
                    var angle = -MathF.PI / 2 + (i - 1.5f) * .5f;
                    var r = 8 + t * 6;
                    float px = x + MathF.Cos(angle) * r, py = y - 4 + MathF.Sin(angle) * r;
                    var color = new SKColor(255, 255, 255, Fade());
                    Dot(px, py, color); Dot(px + MathF.Cos(angle), py + MathF.Sin(angle), color);
                }
                break;
            case ParkEffectKind.Lick:
                for (var i = 0; i < 3; i++) Dot(x - 4 + i * 4, y + t * 8 + i, new SKColor(255, 150, 190, Fade()), 2);
                break;
            case ParkEffectKind.Psychic:
                for (var ring = 0; ring < 2; ring++)
                {
                    var r = (t * 40 + ring * 12) % 40;
                    paint.Color = new SKColor(240, 120, 200, (byte)(200 * (1 - r / 40)));
                    paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 1;
                    canvas.DrawOval(x, y + 6, r, r * .5f, paint);
                    paint.Style = SKPaintStyle.Fill;
                }
                break;
            case ParkEffectKind.Hearts:
                for (var i = 0; i < 3; i++)
                {
                    var rise = t * 14 + i * 2;
                    float px = x - 6 + i * 6, py = y - 2 - rise;
                    var color = new SKColor(230, 80, 110, Fade(.4f));
                    Dot(px, py, color); Dot(px + 2, py, color); Dot(px, py + 1, color, 3); Dot(px + 1, py + 2, color);
                }
                break;
            case ParkEffectKind.Rustle:
                // Leaves shake loose where something is hiding.
                for (var i = 0; i < 6; i++)
                {
                    var wobble = MathF.Sin(age / 60f + i * 1.3f) * 3;
                    Dot(x - 7 + i * 3 + wobble, y - 2 + (i % 2) * 3 + t * 6, new SKColor(110, 190, 80, Fade()), 2);
                }
                break;
            case ParkEffectKind.Gust:
            {
                // Wings beating at the ground: streaks of wind blown out to both sides and a little
                // dust, at the feet, over in half the usual time.
                var g = Math.Clamp(t * 2, 0, 1);
                var alpha = (byte)(220 * (1 - g));
                for (var side = -1; side <= 1; side += 2)
                    for (var i = 0; i < 3; i++)
                    {
                        var reach = 5 + g * (10 + i * 4);
                        paint.Color = new SKColor(236, 240, 246, alpha);
                        canvas.DrawRect(MathF.Round(x + side * reach - (side < 0 ? 4 : 0)), MathF.Round(y - 1 - i * 2 + g * i), 4, 1, paint);
                    }
                for (var i = 0; i < 6; i++)
                    Dot(x + (i - 2.5f) * (2 + g * 4), y - MathF.Sin(g * MathF.PI) * (2 + i % 3), new SKColor(150, 132, 104, alpha), 1);
                break;
            }
            case ParkEffectKind.Dirt:
                for (var i = 0; i < 8; i++)
                {
                    var dx = (i - 3.5f) * 2.2f;
                    var h = MathF.Sin(Math.Min(1, t * 1.6f) * MathF.PI) * (6 + i % 3 * 3);
                    Dot(x + dx * (1 + t * 1.5f), y + 10 - h, new SKColor(120, 85, 50, Fade()), i % 2 + 1);
                }
                break;
            case ParkEffectKind.Sparkle:
                for (var i = 0; i < 5; i++)
                {
                    var angle = i * MathF.PI * 2 / 5 + t * 2;
                    var r = 5 + t * 9;
                    float px = x + MathF.Cos(angle) * r, py = y + MathF.Sin(angle) * r * .7f;
                    var color = new SKColor(255, 250, 200, Fade());
                    Dot(px, py, color); Dot(px - 1, py, color); Dot(px + 1, py, color); Dot(px, py - 1, color); Dot(px, py + 1, color);
                }
                break;
            case ParkEffectKind.Shine:
                // The shiny stars of the games: three four-pointed stars that open and close one
                // after the other around the Pokémon.
                for (var i = 0; i < 3; i++)
                {
                    var local = Math.Clamp(t * 3 - i * .5f, 0, 1);
                    var size = MathF.Round(MathF.Sin(local * MathF.PI) * 3);
                    if (size <= 0) continue;
                    float sx = x + (i - 1) * 8, sy = y - 4 + (i == 1 ? -6 : 2);
                    var color = new SKColor(255, 255, 230, 240);
                    for (var d = -size; d <= size; d++) { Dot(sx + d, sy, color); Dot(sx, sy + d, color); }
                    Dot(sx - 1, sy - 1, color); Dot(sx + 1, sy - 1, color); Dot(sx - 1, sy + 1, color); Dot(sx + 1, sy + 1, color);
                }
                break;
            case ParkEffectKind.Teleport:
            {
                // A ring of light closing in, then a white flash and a few sparks.
                var r = MathF.Max(0, 14 * (1 - t * 2.2f));
                for (var i = 0; i < 12 && r > 0; i++)
                {
                    var angle = i * MathF.PI / 6 + t * 6;
                    Dot(x + MathF.Cos(angle) * r, y + MathF.Sin(angle) * r * .7f, new SKColor(220, 200, 255, Fade(.2f)));
                }
                if (t is > .4f and < .55f)
                    for (var dy = -8; dy <= 4; dy++) Dot(x, y + dy, new SKColor(255, 255, 255, 230));
                for (var i = 0; i < 4 && t > .45f; i++)
                    Dot(x + MathF.Cos(i * 1.7f) * (4 + t * 12), y + MathF.Sin(i * 1.7f) * (3 + t * 8), new SKColor(255, 240, 255, Fade()));
                break;
            }
            case ParkEffectKind.Smoke:
                // Puffs of violet smoke drifting up and spreading.
                for (var i = 0; i < 7; i++)
                {
                    var rise = t * (8 + i % 3 * 4);
                    float px = x + (i - 3) * 2.5f * (1 + t), py = y + 4 - rise;
                    var color = new SKColor(120, 80, 150, (byte)(Fade(.3f) * .8f));
                    Dot(px, py, color, 2); Dot(px + 1, py - 1, color);
                }
                break;
            case ParkEffectKind.Wave:
                // Rings of white water spreading wide over the surface.
                for (var ring = 0; ring < 3; ring++)
                {
                    var r = 6 + t * 26 + ring * 6;
                    for (var i = 0; i < 20; i++)
                    {
                        var angle = i * MathF.PI / 10;
                        Dot(x + MathF.Cos(angle) * r, y + 6 + MathF.Sin(angle) * r * .35f, new SKColor(220, 240, 255, Fade(.3f)));
                    }
                }
                break;
            case ParkEffectKind.Barrier:
            {
                // A pale psychic wall, a curve of light that shimmers and fades.
                for (var i = -6; i <= 6; i++)
                {
                    var shimmer = (age / 60 + i) % 4 == 0 ? 60 : 0;
                    var color = new SKColor(255, 190, 230, (byte)Math.Max(0, Fade(.5f) - shimmer));
                    Dot(x + i * 1.5f, y - 10 - MathF.Abs(i) * .6f, color);
                    Dot(x + i * 1.5f, y - 4 - MathF.Abs(i) * .3f, color);
                }
                break;
            }
            case ParkEffectKind.Nightmare:
                // A slow dark swirl over a sleeper's head.
                for (var i = 0; i < 8; i++)
                {
                    var angle = i * MathF.PI / 4 + t * 5;
                    var r = 4 + i % 2 * 2;
                    Dot(x + MathF.Cos(angle) * r, y - 6 + MathF.Sin(angle) * r * .6f, new SKColor(60, 30, 80, Fade(.4f)), 2);
                }
                break;
            case ParkEffectKind.Aura:
                // Blue aura flickering up around the body.
                for (var i = 0; i < 8; i++)
                {
                    if ((age / 90 + i) % 2 == 0) continue;
                    float px = x + (i - 3.5f) * 2, py = y + 4 - (i * 7 % 5) - t * 10;
                    Dot(px, py, new SKColor(90, 170, 255, Fade(.4f))); Dot(px, py - 1, new SKColor(170, 220, 255, Fade(.4f)));
                }
                break;
            case ParkEffectKind.Crackle:
                // A couple of little sparks off the cheeks, no flash.
                for (var i = 0; i < 3; i++)
                {
                    if ((age / 80 + i) % 2 == 0) continue;
                    float px = x + (i == 0 ? -6 : i == 1 ? 6 : 0), py = y - 2 - i;
                    var color = new SKColor(255, 240, 90, Fade());
                    Dot(px, py, color); Dot(px + 1, py + 1, color); Dot(px, py + 2, color);
                }
                break;
            case ParkEffectKind.Spray:
                // A jet of droplets bursting out and falling.
                for (var i = 0; i < 9; i++)
                {
                    var spread = (i - 4) * 1.4f;
                    float px = x + spread * (1 + t * 3), py = y - 8 * MathF.Sin(MathF.Min(1, t * 2) * MathF.PI) + t * 8 + MathF.Abs(spread) * .5f;
                    Dot(px, py, new SKColor(150, 210, 255, Fade(.5f)));
                }
                break;
            case ParkEffectKind.Miss:
                // A small puff where the player looked: nothing there.
                for (var i = 0; i < 4; i++)
                {
                    var angle = i * MathF.PI / 2 + .4f;
                    Dot(x + MathF.Cos(angle) * (2 + t * 6), y + MathF.Sin(angle) * (2 + t * 4), new SKColor(240, 240, 240, Fade(.3f)));
                }
                break;
            case ParkEffectKind.Coin or ParkEffectKind.Egg:
            {
                var h = MathF.Sin(Math.Min(1, t * 2) * MathF.PI) * 10;
                float px = x - 1, py = y - 6 - h;
                var (body, shine) = effect.Kind == ParkEffectKind.Coin
                    ? (new SKColor(240, 190, 50, Fade()), new SKColor(255, 245, 180, Fade()))
                    : (new SKColor(250, 245, 230, Fade()), new SKColor(255, 255, 255, Fade()));
                Dot(px, py, body, 3); Dot(px - 1, py + 1, body); Dot(px + 3, py + 1, body); Dot(px, py + 3, body, 3);
                if (effect.Kind == ParkEffectKind.Egg) Dot(px, py - 1, body, 3);
                Dot(px, py, shine);
                if ((age / 100) % 2 == 0) { Dot(px + 5, py - 3, shine); Dot(px - 3, py - 2, shine); }
                break;
            }
        }
    }
}
