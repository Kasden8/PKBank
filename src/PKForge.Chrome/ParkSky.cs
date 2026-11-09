using PKForge.Domain;
using SkiaSharp;

namespace PKForge.Chrome;

/// <summary>The light of the day over the park, in art pixels: a pink morning, a golden evening,
/// a blue night with the cabin window lit and fireflies over the grass.</summary>
public static class ParkSky
{
    // The home park's cabin window and the row its wall stands on, in the home park's own pixels.
    private static readonly SKRect CabinWindow = new(296, 50, 309, 59);
    private const float CabinBase = 67;

    /// <summary>The day's light as a color multiplied into everything drawn (null at midday).
    /// Applied per sprite, so a lit window can still sit between the cabin and a Pokémon in front of it.</summary>
    public static SKColorFilter? Light(ParkDayPhase phase, DateTime local, ParkWeather weather = ParkWeather.Sunny) =>
        LightColor(phase, local, weather) is var color && color == SKColors.White ? null : SKColorFilter.CreateBlendMode(color, SKBlendMode.Modulate);

    /// <summary>The colour the day's light multiplies everything by (white: none). The evening
    /// darkens in six steps, so the map's lit art is redrawn only now and then.</summary>
    public static SKColor LightColor(ParkDayPhase phase, DateTime local, ParkWeather weather = ParkWeather.Sunny)
    {
        var color = SKColors.White;
        switch (phase)
        {
            case ParkDayPhase.Morning: color = new SKColor(255, 236, 240); break;
            case ParkDayPhase.Evening:
                var t = MathF.Round(Math.Clamp((local.Hour - 18 + local.Minute / 60f) / 3, 0, 1) * 6) / 6;
                color = PksmPaint.Mix(new SKColor(255, 228, 196), new SKColor(214, 168, 150), t);
                break;
            case ParkDayPhase.Night: color = new SKColor(112, 126, 196); break;
        }
        // Clouds grey the light; rain and storms darken it.
        var sky = weather switch
        {
            ParkWeather.Cloudy => new SKColor(220, 222, 230), ParkWeather.Rain => new SKColor(182, 188, 206),
            ParkWeather.Storm => new SKColor(150, 152, 176), _ => SKColors.White
        };
        return new SKColor((byte)(color.Red * sky.Red / 255), (byte)(color.Green * sky.Green / 255), (byte)(color.Blue * sky.Blue / 255));
    }

    /// <summary>Rain over the whole map, in art pixels: slanted streaks and rings on the lake.</summary>
    public static void Rain(SKCanvas canvas, SKPaint paint, ParkMap map, ParkWeather weather, long time)
    {
        if (weather is not (ParkWeather.Rain or ParkWeather.Storm)) return;
        var drops = weather == ParkWeather.Storm ? 140 : 90;
        paint.Color = new SKColor(200, 215, 255, 120);
        for (var i = 0; i < drops; i++)
        {
            var seed = i * 7919 + 17;
            var speed = 160 + seed % 60;
            var x = (seed * 37 % map.Width + time / 1000f * 40) % map.Width;
            var y = (seed * 53 % map.Height + time / 1000f * speed) % (map.Height + 10) - 10;
            canvas.DrawRect(MathF.Round(x), MathF.Round(y), 1, 3, paint);
            canvas.DrawRect(MathF.Round(x) + 1, MathF.Round(y) + 3, 1, 2, paint);
        }
    }

    /// <summary>How bright a storm's lightning is now, 0 to 1: a flash every twelve to twenty seconds.</summary>
    public static float Lightning(ParkWeather weather, long time)
    {
        if (weather != ParkWeather.Storm) return 0;
        var cycle = time % 16_000;
        return cycle < 90 ? 1 : cycle is >= 180 and < 240 ? .6f : 0;
    }

    /// <summary>The lit cabin window, drawn in depth order right after the cabin: Pokémon in front hide it.</summary>
    public static (float Depth, Action Draw)? Window(SKCanvas canvas, SKPaint paint, ParkMap map, ParkDayPhase phase)
    {
        if (phase is not (ParkDayPhase.Evening or ParkDayPhase.Night)) return null;
        var window = CabinWindow;
        window.Offset(map.HomeOrigin.X, map.HomeOrigin.Y);
        return (map.HomeOrigin.Y + CabinBase + .5f, () =>
        {
            var filter = paint.ColorFilter;
            paint.ColorFilter = null;
            paint.Color = new SKColor(255, 214, 120, phase == ParkDayPhase.Night ? (byte)235 : (byte)160);
            canvas.DrawRect(window, paint);
            paint.Color = new SKColor(255, 200, 110, phase == ParkDayPhase.Night ? (byte)45 : (byte)22);
            canvas.DrawOval(window.MidX, window.Bottom + 14, 16, 6, paint);
            paint.ColorFilter = filter;
        });
    }

    /// <summary>The campfire, in depth order with the residents: two crossed logs, a warm pool of light
    /// on the grass, and pixel flames that flicker, rise and fade, with the odd spark. It grows for
    /// the first seconds after it is lit and sinks to embers at the end.</summary>
    public static (float Depth, Action Draw)? Campfire(SKCanvas canvas, SKPaint paint, ParkCampfire? fire, long now, long time)
    {
        if (fire is null) return null;
        var x = MathF.Round(fire.At.X);
        var y = MathF.Round(fire.At.Y);
        var size = Math.Clamp((now - fire.LitAt) / 2500f, .25f, 1) * Math.Clamp((fire.Until - now) / 20_000f, .3f, 1);
        return (y, () =>
        {
            var filter = paint.ColorFilter;
            paint.ColorFilter = null;
            var flicker = .85f + .15f * MathF.Sin(time / 90f) * MathF.Sin(time / 37f);
            paint.Color = new SKColor(255, 170, 80, (byte)(46 * size * flicker));
            canvas.DrawOval(x, y, 30 * size + 6, 11 * size + 3, paint);
            paint.Color = new SKColor(255, 190, 100, (byte)(40 * size * flicker));
            canvas.DrawOval(x, y, 16 * size + 4, 6 * size + 2, paint);
            paint.ColorFilter = filter;
            // The logs take the evening light like everything else.
            paint.Color = new SKColor(0x5a, 0x36, 0x22);
            canvas.DrawRect(x - 7, y - 2, 14, 3, paint);
            canvas.DrawRect(x - 2, y - 4, 4, 6, paint);
            paint.Color = new SKColor(0x7a, 0x4e, 0x30);
            canvas.DrawRect(x - 7, y - 2, 14, 1, paint);
            paint.ColorFilter = null;
            // Embers and the core of the fire.
            paint.Color = new SKColor(255, 110, 40, (byte)(200 * flicker));
            canvas.DrawRect(x - 4, y - 3, 8, 2, paint);
            var core = MathF.Round(4 + 4 * size * flicker);
            paint.Color = new SKColor(255, 150, 50);
            canvas.DrawRect(x - 3, y - 2 - core, 6, core, paint);
            paint.Color = new SKColor(255, 225, 120);
            canvas.DrawRect(x - 1, y - 1 - MathF.Round(core * .7f), 3, MathF.Round(core * .7f), paint);
            // Flames that rise, shrink and cool from yellow to red.
            for (var i = 0; i < 7; i++)
            {
                var life = ((time + i * 157) % 760) / 760f;
                var height = (6 + 12 * size) * life;
                var drift = MathF.Round(MathF.Sin(time / 210f + i * 1.9f) * 2.5f * life + (i % 3 - 1) * 2);
                var px = x + drift;
                var py = MathF.Round(y - 3 - height);
                var side = life < .35f ? 3 : life < .7f ? 2 : 1;
                paint.Color = life < .3f ? new SKColor(255, 230, 120) : life < .65f ? new SKColor(255, 150, 50) : new SKColor(230, 70, 40, (byte)(255 * (1 - life) * 2));
                canvas.DrawRect(px - side / 2, py, side, side, paint);
            }
            // A spark now and then.
            if (time % 1300 < 500)
            {
                var spark = (time % 1300) / 500f;
                paint.Color = new SKColor(255, 220, 140, (byte)(255 * (1 - spark)));
                canvas.DrawRect(x + MathF.Round(MathF.Sin(time / 1300 * 2.1f) * 6), MathF.Round(y - 14 - 16 * spark), 1, 1, paint);
            }
            paint.ColorFilter = filter;
        });
    }

    /// <summary>Fireflies drift over the meadow at night and blink out of step.</summary>
    public static void Fireflies(SKCanvas canvas, SKPaint paint, ParkMap map, ParkDayPhase phase, long time)
    {
        if (phase != ParkDayPhase.Night) return;
        for (var i = 0; i < 9; i++)
        {
            var seed = i * 7919;
            var baseX = (seed * 37 % 1000) / 1000f * map.Width;
            var baseY = 40 + (seed * 53 % 1000) / 1000f * (map.Height - 60);
            var t = time / 1000f + i * 1.7f;
            var x = baseX + MathF.Sin(t * .6f + i) * 10;
            var y = baseY + MathF.Sin(t * .9f + i * 2) * 5;
            var glow = (MathF.Sin(t * 2.3f + i) + 1) / 2;
            if (glow < .25f) continue;
            paint.Color = new SKColor(230, 255, 140, (byte)(70 * glow));
            canvas.DrawRect(MathF.Round(x) - 1, MathF.Round(y) - 1, 3, 3, paint);
            paint.Color = new SKColor(250, 255, 190, (byte)(255 * glow));
            canvas.DrawRect(MathF.Round(x), MathF.Round(y), 1, 1, paint);
        }
    }
}
