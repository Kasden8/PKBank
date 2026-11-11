using System.Runtime.CompilerServices;
using Avalonia.Platform;
using PKForge.Chrome;
using PKForge.Domain;
using SkiaSharp;

namespace PKBank.App.Services.Pokepark;

/// <summary>
/// The park on screen: the map, its life and the camera. Ported from PKForge's scene, drawn
/// with Skia into a bitmap the Avalonia side shows. The world is painted at its own resolution
/// (one art pixel per pixel) and scaled up nearest-neighbour, so it keeps its pixel look.
/// </summary>
public sealed class ParkRenderer(PokeparkSpriteService walking)
{
    private readonly object _artGate = new();
    private ParkArt? _art;
    private Task? _artLoad;
    private readonly ParkLowRes _lowRes = new();
    private long? _cameraTime;
    private ParkCamera _camera = new(1, 0, 0);
    private float _drawWidth = 1, _drawHeight = 1;
    private readonly Dictionary<string, ParkPokemon> _visitors = new();
    private readonly ConditionalWeakTable<SKBitmap, FrameBounds> _visibleBounds = new();

    private sealed class FrameBounds(SKColor[] pixels)
    {
        public SKColor[] Pixels { get; } = pixels;
        public Dictionary<SKRect, SKRect> Frames { get; } = new();
    }

    /// <summary>The park's map.</summary>
    public static ParkMap Map => ParkMap.Park;

    /// <summary>The camera: the player drags it and it follows the selected resident.</summary>
    public ParkViewport Viewport { get; } = new(ParkMap.Park.Width, ParkMap.Park.Height);

    /// <summary>The residents as the renderer knows them.</summary>
    public IReadOnlyList<ParkPokemon> Residents { get; set; } = [];

    /// <summary>The simulation being drawn.</summary>
    public ParkLife? Life { get; set; }

    /// <summary>The selected resident (the one the camera follows).</summary>
    public string? SelectedId { get; set; }

    /// <summary>Words over the Pokémon that just did something.</summary>
    public bool ShowCaptions { get; set; } = true;

    private static readonly SKColor Background = SKColor.Parse("#233a39");

    /// <summary>Loads the park's art once; a failed load is tried again on the next call.</summary>
    public async Task LoadArtAsync()
    {
        Task load;
        lock (_artGate)
        {
            if (_art is not null) return;
            load = _artLoad ??= Task.Run(() =>
            {
                // The ways across the park for walkers and swimmers are worked out here, off
                // the screen's thread, not on the first frame.
                foreach (var way in new[] { ParkTraversal.Land, ParkTraversal.Land | ParkTraversal.Water, ParkTraversal.Water })
                    ParkNavigator.For(Map, way);
                var art = CutMapArt();
                lock (_artGate) _art = art;
            });
        }
        try { await load.ConfigureAwait(false); }
        catch
        {
            lock (_artGate) _artLoad = null;
            throw;
        }
    }

    /// <summary>The park's art, cut into depth-sorted pieces (the map's three layers).</summary>
    public static ParkArt CutMapArt()
    {
        var ground = LoadAsset("Assets/park/ground.png");
        var objects = LoadAsset("Assets/park/objects.png");
        var depth = LoadAsset("Assets/park/depth.png");
        if (ground is null || objects is null)
        {
            ground?.Dispose();
            objects?.Dispose();
            depth?.Dispose();
            throw new IOException("The park's map art is missing.");
        }
        // ParkArt.Cut takes ownership of the layers: they must not be disposed here.
        return ParkArt.Cut(ground, objects, depth);
    }

    private static SKBitmap? LoadAsset(string logicalPath)
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://PKBank/" + logicalPath));
            using var data = SKData.Create(stream);
            return SKBitmap.Decode(data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Draws one frame of the park.</summary>
    public void Draw(SKCanvas canvas, int width, int height, long time)
    {
        var life = Life;
        if (life is null || width <= 0 || height <= 0) return;
        var map = Map;
        var now = DateTime.Now;
        life.Phase = ParkCare.PhaseAt(now);
        life.Weather = ParkCare.WeatherAt(now);
        life.Today = DateOnly.FromDateTime(now);

        var actors = life.Actors.Where(a => !a.Indoors && !a.Hidden && !a.InShadow && !a.Submerged).ToArray();
        var camera = ParkCamera.Fit(map, width, height, new SKPoint(map.Width / 2f, map.Height / 2f));
        // The first frame jumps to the selected resident; later frames glide.
        var elapsed = _cameraTime is { } last ? time - last : 1000;
        if (_cameraTime is null)
        {
            // The park opens on the home region, where the residents live.
            var home = map.Regions.FirstOrDefault(r => r.Id == "home");
            Viewport.Follow(home?.Center.X ?? map.Width / 2f, home?.Center.Y ?? map.Height / 2f);
        }
        _cameraTime = time;
        Viewport.Resize(width, height);
        Viewport.Step(elapsed);
        if (actors.FirstOrDefault(a => a.Resident.Id == SelectedId) is { } selected)
            Viewport.Track(selected.X, selected.Y - selected.Altitude - 12, elapsed);
        _camera = camera = ParkCamera.Of(Viewport);
        _drawWidth = width; _drawHeight = height;

        // The world at its own resolution, scaled up once (see ParkLowRes).
        var screen = canvas;
        canvas = _lowRes.Begin(camera, map.Width, map.Height, width, height, Background);
        using var paint = new SKPaint { IsAntialias = false, Color = SKColors.White };
        // The day's light: the map's art is kept tinted (redrawn only when the light changes),
        // the sprites are tinted as they are drawn.
        var lightColor = ParkSky.LightColor(life.Phase, now, life.Weather);
        using var light = lightColor == SKColors.White ? null : SKColorFilter.CreateBlendMode(lightColor, SKBlendMode.Modulate);
        using var mapPaint = new SKPaint { IsAntialias = false, Color = SKColors.White };
        var visible = camera.Visible(width, height);
        ParkArt? art;
        lock (_artGate) art = _art;
        var lit = art?.LitBy(lightColor);
        if (lit is { } lit0) ParkPaint.Ground(canvas, lit0.Ground, visible, mapPaint);
        paint.ColorFilter = light;
        var heads = new float[actors.Length];
        // A flyer in the air leaves its shadow on the ground, in depth order, and is drawn over
        // everything else.
        var layers = actors.Select((actor, k) => (actor.InFlight ? float.MaxValue : actor.Y,
            (Action)(() => heads[k] = DrawResident(canvas, paint, life, actor, time, actor.Resident.Id == SelectedId)))).ToList();
        foreach (var diver in life.Actors.Where(a => a.Submerged))
            layers.Add((diver.Y, () => DrawUnderwater(canvas, paint, life, diver, time)));
        foreach (var flyer in actors.Where(a => a.InFlight))
            layers.Add((flyer.Y, () => DrawFlightShadow(canvas, paint, life, flyer)));
        if (ParkSky.Window(canvas, paint, map, life.Phase) is { } window) layers.Add(window);
        if (ParkSky.Campfire(canvas, paint, life.Campfire, life.Now, time) is { } fire) layers.Add(fire);
        if (life.Chronicle.Dam > 0 && life.DamSpot is { } dam)
            layers.Add((dam.Y + 10, () => ParkProps.DrawDam(canvas, paint, dam.X + 12, dam.Y + 10, life.Chronicle.Dam)));
        ParkPaint.InDepthOrder(canvas, lit?.Pieces ?? [], visible, mapPaint, layers);
        DrawGlows(canvas, life, actors, now);
        DrawBall(canvas, paint, life, time);
        paint.ColorFilter = null;
        foreach (var effect in life.Effects) ParkEffects.Draw(canvas, paint, effect, time);
        if (life.Weather is ParkWeather.Sunny or ParkWeather.Cloudy) ParkSky.Fireflies(canvas, paint, map, life.Phase, time);
        ParkSky.Rain(canvas, paint, map, life.Weather, time);

        // Emotes and the name tag float above everything.
        ParkActor? named = null;
        for (var k = 0; k < actors.Length; k++)
        {
            var actor = actors[k];
            // A fresh emote shows even on a sleeper (a pat makes it smile); otherwise sleepers
            // show Zs.
            var fresh = actor.Emote != ParkEmote.None && time - actor.EmoteSince < 2600;
            if (actor.Pose == ParkPose.Sleep && !fresh)
            {
                // Zs, and now and then a dream: of food when hungry, of you when it loves you.
                var dreaming = (time - actor.PoseSince) / 4000 % 3 == 2;
                if (dreaming)
                    ParkEmotes.DrawDream(canvas, paint, actor.Care.Hunger < 40 ? ParkEmotes.Dream.Berry : actor.Care.Bond >= 60 ? ParkEmotes.Dream.Heart
                        : actor.Resident.Temperament == ParkTemperament.Playful ? ParkEmotes.Dream.Ball : ParkEmotes.Dream.Star,
                        actor.X + 4, heads[k], (time - actor.PoseSince) % 4000);
                else ParkEmotes.Draw(canvas, paint, ParkEmote.Sleep, actor.X + 4, heads[k], time - actor.PoseSince);
            }
            else if (fresh)
                ParkEmotes.Draw(canvas, paint, actor.Emote, actor.X + 5, heads[k] - 1, time - actor.EmoteSince);
            if (actor.Resident.Id == SelectedId) named = actor;
        }
        _lowRes.End(screen, camera);
        // The name tag is text: drawn at the screen's own resolution, over the scaled-up world.
        if (named is not null)
            DrawLabel(screen, paint, named.Resident.Name, (named.X - camera.Left) * camera.Scale, (named.Y - named.Altitude - camera.Top) * camera.Scale, width, height);
        if (ShowCaptions) DrawCaptions(screen, actors, heads, camera, width, height, SelectedId);
        DrawGameBanner(screen, paint, life, width, time);
        // A zap, or the storm's lightning, flashes the whole screen for an instant.
        if (Math.Max(ParkEffects.Flash(life.Effects, time), ParkSky.Lightning(life.Weather, time)) is > 0 and var flash)
        {
            paint.Color = new SKColor(255, 250, 220, (byte)(150 * flash));
            screen.DrawRect(0, 0, width, height, paint);
        }
    }

    /// <summary>What the game asks of the player, at the top of the view: what to do, time left.</summary>
    private static void DrawGameBanner(SKCanvas canvas, SKPaint paint, ParkLife life, int width, long time)
    {
        if (life.Game is not { Stage: not ParkGameStage.Over } game || life[game.HostId] is not { } host)
            return;
        var name = host.Resident.Name;
        var seconds = Math.Max(0, (game.Deadline - life.Now) / 1000);
        var text = (game.Kind, game.Stage) switch
        {
            (ParkGameKind.HideAndSeek, ParkGameStage.Starting) => $"Eyes closed… counting to ten while {name} hides",
            (ParkGameKind.HideAndSeek, _) => $"Find {name}! Click where it hides  ·  {seconds / 60}:{seconds % 60:D2}  ·  {game.Tries} tries",
            (ParkGameKind.Race, ParkGameStage.Starting) => "The racers line up…",
            (ParkGameKind.Race, ParkGameStage.Countdown) => $"{3 - (life.Now - game.StageSince) / 1000}…",
            (ParkGameKind.Race, _) => life.Now - game.StageSince < 900 ? "GO!" : $"Click {name} to cheer it on!  ·  {game.Finished.Count}/{game.Players.Count} at the flag",
            (ParkGameKind.Brush, _) => $"Brush {name} — drag the mouse over it  ·  {(int)(game.Progress * 100)}%",
            (ParkGameKind.Spar, ParkGameStage.Countdown) => $"{name} takes its stance…",
            (ParkGameKind.Spar, _) => game.Striking ? "BLOCK! (click)" : $"Round {Math.Min(game.Round + 1, ParkLife.SparRounds)}/{ParkLife.SparRounds}  ·  {game.Blocked} blocked  ·  click when it strikes",
            _ => $"{name} is digging…",
        };
        var unit = width / 1920f;
        using var font = PixelFont.For(text, 40 * unit);
        var w = font.MeasureText(text) + 60 * unit;
        var rect = SKRect.Create((width - w) / 2, 24 * unit, w, 64 * unit);
        paint.Color = new SKColor(0x10, 0x16, 0x30, 220);
        canvas.DrawRoundRect(rect, 18 * unit, 18 * unit, paint);
        if (game.Kind == ParkGameKind.Brush && game.Progress > 0)
        {
            paint.Color = new SKColor(0x6c, 0xd0, 0x7a, 180);
            canvas.DrawRoundRect(SKRect.Create(rect.Left, rect.Bottom - 8 * unit, rect.Width * game.Progress, 8 * unit), 4 * unit, 4 * unit, paint);
        }
        paint.Color = new SKColor(0xf4, 0xf6, 0xff);
        canvas.DrawText(text, rect.MidX, rect.MidY + 14 * unit, SKTextAlign.Center, font, paint);
    }

    /// <summary>After dark the Fire types' flames, Ampharos's tail and the like light up the
    /// ground round them: brighter when they are happy, dimmer asleep.</summary>
    private static void DrawGlows(SKCanvas canvas, ParkLife life, IReadOnlyList<ParkActor> actors, DateTime now)
    {
        var dark = life.Phase switch { ParkDayPhase.Night => 1f, ParkDayPhase.Evening => .6f, _ => 0f };
        if (dark <= 0) return;
        using var paint = new SKPaint { BlendMode = SKBlendMode.Screen, IsAntialias = false };
        foreach (var actor in actors)
        {
            if (ParkLife.GlowOf(actor.Resident) is not { } glow || actor.Camouflaged) continue;
            var mood = actor.IsAsleep ? .55f : actor.ActivityMoodIsBright ? 1.25f : 1f;
            var radius = glow.Radius * mood;
            var color = new SKColor((byte)(glow.Color >> 16), (byte)(glow.Color >> 8), (byte)glow.Color);
            var center = new SKPoint(actor.X, actor.Y - 8 - actor.Altitude);
            // A slow flicker, each its own.
            var flicker = .85f + .15f * MathF.Sin((float)(now.TimeOfDay.TotalMilliseconds / 260 + actor.Resident.Id.Length));
            using var shader = SKShader.CreateRadialGradient(center, radius,
                [color.WithAlpha((byte)(150 * dark * flicker * actor.OpacityAt(life.Now))), color.WithAlpha(0)], SKShaderTileMode.Clamp);
            paint.Shader = shader;
            canvas.DrawCircle(center, radius, paint);
        }
    }

    /// <summary>A giant deep under the water: its great dark shape, drifting.</summary>
    private void DrawUnderwater(SKCanvas canvas, SKPaint paint, ParkLife life, ParkActor actor, long time)
    {
        var mon = LookOf(actor);
        if (mon is not null && walking.GetFrame(mon.Species, mon.Form, mon.Shiny, actor.Pose, actor.Facing, time - actor.PoseSince) is { } frame)
        {
            var middle = new SKPoint(actor.X - frame.Feet.X, actor.Y - frame.Feet.Y);
            ParkWater.Deep(canvas, paint, frame.Bitmap, frame.Source, middle, new SKPoint(actor.X, actor.Y), time);
            return;
        }
        paint.Color = new SKColor(14, 34, 82, 70);
        canvas.DrawOval(actor.X, actor.Y - 2, 20, 7, paint);
    }

    /// <summary>The ground under a flyer: its shadow, smaller and fainter the higher it flies.</summary>
    private void DrawFlightShadow(SKCanvas canvas, SKPaint paint, ParkLife life, ParkActor actor)
    {
        var mon = LookOf(actor);
        var size = mon is null ? 1 : walking.GetShadowSize(mon.Species, mon.Form, mon.Shiny) ?? 1;
        var shrink = 1 - Math.Clamp(actor.Altitude / 60f, 0, .5f);
        if (actor.Resident.Id == SelectedId)
        {
            paint.Color = new SKColor(255, 245, 188, 150);
            canvas.DrawOval(actor.X, actor.Y, 8 * shrink, 3 * shrink, paint);
        }
        paint.Color = new SKColor(0, 0, 0, (byte)(60 * shrink));
        canvas.DrawOval(actor.X, actor.Y, (4 + size * 2) * shrink, (1.5f + size * .5f) * shrink, paint);
    }

    /// <returns>The top of the drawn sprite, for the emote above its head.</returns>
    private float DrawResident(SKCanvas canvas, SKPaint paint, ParkLife life, ParkActor actor, long time, bool selected)
    {
        var mon = LookOf(actor);
        if (mon is null) return actor.Y;
        // Leaping out of the water it shows whole, in the air.
        var swimming = !actor.InFlight && !actor.Riding && actor.Altitude == 0 && life.IsSwimming(actor);
        var lift = MathF.Round(actor.Altitude);
        // Faded out (a Ghost unseen, a teleport) or blended in (Kecleon): barely there.
        var opacity = actor.Camouflaged ? .1f : actor.OpacityAt(life.Now);
        if (opacity < .02f) return actor.Y - 12;
        if (selected && !actor.InFlight)
        {
            paint.Color = new SKColor(255, 245, 188, 180);
            canvas.DrawOval(actor.X, actor.Y, 8, 3, paint);
        }
        // No shadow on the ground for one lying up on top of another (Snorlax's belly).
        if (!swimming && !actor.InFlight && actor.Altitude == 0)
        {
            var size = walking.GetShadowSize(mon.Species, mon.Form, mon.Shiny) ?? 1;
            paint.Color = new SKColor(0, 0, 0, (byte)(60 * opacity));
            canvas.DrawOval(actor.X, actor.Y, 4 + size * 2, 1.5f + size * .5f, paint);
        }
        var elapsed = time - actor.PoseSince;
        // Ditto in disguise wears the other species' sprite, tinted its own purple.
        var (species, form, shiny) = actor.Disguise is { } copy ? (copy, 0, false) : (mon.Species, mon.Form, mon.Shiny);
        if (actor.Disguise is not null) walking.Warm(species, 0, false, static () => { });
        var frame = walking.GetFrame(species, form, shiny, actor.Pose, actor.Facing, elapsed);
        var light = paint.ColorFilter;
        using var dittoTint = actor.Disguise is null || !actor.DisguiseTinted ? null : SKColorFilter.CreateBlendMode(new SKColor(225, 170, 255), SKBlendMode.Modulate);
        using var disguised = dittoTint is null ? null : light is null ? null : SKColorFilter.CreateCompose(light, dittoTint);
        if (dittoTint is not null) paint.ColorFilter = disguised ?? dittoTint;
        float head;
        paint.Color = SKColors.White.WithAlpha((byte)(255 * opacity));
        // In the water: whole above the surface, a little of its body seen through it below.
        var waterline = actor.Y - 4;
        void Sprite(SKBitmap bitmap, SKRect source, SKRect dest)
        {
            if (swimming) ParkWater.Swimmer(canvas, paint, bitmap, source, dest, waterline);
            else canvas.DrawBitmap(bitmap, source, dest, paint);
        }
        if (frame is not null)
        {
            // Drawn 1:1 on whole art pixels, the PMD shadow's middle on the feet.
            var source = frame.Source;
            var middleX = actor.X - frame.Feet.X;
            var middleY = actor.Y - lift - frame.Feet.Y + (swimming ? 4 : 0);
            Sprite(frame.Bitmap, source, SKRect.Create(MathF.Round(middleX - source.Width / 2),
                MathF.Round(middleY - source.Height / 2), source.Width, source.Height));
            head = middleY + frame.Bounds.Top;
        }
        else if (BundledSprite(mon.Species, mon.Form, mon.Shiny) is { } bitmap)
        {
            var source = VisibleBounds(bitmap, new SKRect(0, 0, bitmap.Width, bitmap.Height));
            var scale = Math.Min(.5f, 32 / source.Height);
            var w = source.Width * scale;
            var h = source.Height * scale;
            var bob = actor.IsWalking ? MathF.Round((float)Math.Sin(time / 140d)) : 0;
            var bottom = actor.Y - lift + (swimming ? 4 : 0);
            Sprite(bitmap, source, SKRect.Create(MathF.Round(actor.X - w / 2), MathF.Round(bottom - h + bob), w, h));
            head = bottom - h;
        }
        else
        {
            paint.Color = SKColor.Parse("#f8efcf");
            canvas.DrawCircle(actor.X, actor.Y - 7, 6, paint);
            paint.Color = SKColor.Parse("#65855e");
            canvas.DrawRect(actor.X - 3, actor.Y - 9, 1, 1, paint);
            canvas.DrawRect(actor.X + 2, actor.Y - 9, 1, 1, paint);
            head = actor.Y - 13;
        }
        if (swimming) DrawRipples(canvas, paint, actor.X, waterline, time);
        paint.ColorFilter = light;
        if (actor.Prop != ParkProp.None) ParkProps.Draw(canvas, paint, actor.Prop, actor.X, actor.Y, actor.Facing, elapsed, actor.BerryFlavor);
        return head;
    }

    /// <summary>Wild visitors have no roster entry: one is made up for drawing, and their
    /// sprites load on first sight.</summary>
    private ParkPokemon? LookOf(ParkActor actor)
    {
        var known = Residents.FirstOrDefault(r => r.Id == actor.Resident.Id);
        if (known is not null) return known;
        if (!ParkLife.IsVisitor(actor.Resident.Id)) return null;
        if (!_visitors.TryGetValue(actor.Resident.Id, out var look))
        {
            // A released Pokémon back from the Wilds keeps its form and its colors.
            var friend = Life?.OldFriendOf(actor.Resident.Id);
            _visitors[actor.Resident.Id] = look = new ParkPokemon(actor.Resident.Id, actor.Resident.Species,
                friend?.Form ?? 0, friend?.Shiny ?? actor.Resident.Shiny, actor.Resident.Name,
                ParkLife.IsKeeper(actor.Resident.Id) ? "Daycare" : friend is null ? "Wild" : "Old friend");
            walking.Warm(look.Species, look.Form, look.Shiny, static () => { });
        }
        return look;
    }

    /// <summary>The bundled PKHeX sprite, when the PMD one has not loaded (or there is no network).</summary>
    private static SKBitmap? BundledSprite(int species, int form, bool shiny)
    {
        var look = new SpriteLook(species, form, shiny);
        foreach (var candidate in SpriteCatalog.BundledCandidates(look))
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", candidate.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            using var data = SKData.Create(SKFileStream.OpenStream(path));
            var bitmap = SKBitmap.Decode(data);
            if (bitmap is not null) return bitmap;
        }
        return null;
    }

    /// <summary>The thrown ball: an arc through the air, then on the ground, then carried.</summary>
    private static void DrawBall(SKCanvas canvas, SKPaint paint, ParkLife life, long time)
    {
        if (life.Ball is not { } ball) return;
        if (ball.HolderId is { } holder && life[holder] is { } carrier)
        {
            var side = carrier.Facing is 1 or 2 or 3 ? 6 : carrier.Facing is 5 or 6 or 7 ? -6 : 0;
            ParkProps.DrawBall(canvas, paint, carrier.X + side, carrier.Y + 1, 4);
            return;
        }
        var t = Math.Clamp((time - ball.ThrownAt) / (float)ParkBall.FlightMilliseconds, 0, 1);
        var x = ball.From.X + (ball.To.X - ball.From.X) * t;
        var y = ball.From.Y + (ball.To.Y - ball.From.Y) * t;
        var height = t < 1 ? MathF.Sin(t * MathF.PI) * 34
            : MathF.Abs(MathF.Sin((time - ball.ThrownAt - ParkBall.FlightMilliseconds) / 120f)) * 4 * MathF.Max(0, 1 - (time - ball.ThrownAt - ParkBall.FlightMilliseconds) / 500f);
        ParkProps.DrawBall(canvas, paint, x, y, height);
    }

    // Two light rings spread from the swimmer, one after the other.
    private static void DrawRipples(SKCanvas canvas, SKPaint paint, float x, float y, long time)
    {
        for (var i = 0; i < 2; i++)
        {
            var t = (time / 1600f + i * .5f) % 1;
            var radius = 5 + MathF.Round(t * 5);
            paint.Color = new SKColor(0xd8, 0xf0, 0xff, (byte)(200 * (1 - t)));
            canvas.DrawRect(MathF.Round(x - radius), MathF.Round(y), radius * 2, 1, paint);
            canvas.DrawRect(MathF.Round(x - radius + 2), MathF.Round(y + 1), radius * 2 - 4, 1, paint);
        }
    }

    private const long CaptionFor = 4000;

    /// <summary>What a Pokémon just did, in a few words over its head for four seconds.</summary>
    private static void DrawCaptions(SKCanvas canvas, ParkActor[] actors, float[] heads, ParkCamera camera,
        int screenWidth, int screenHeight, string? selectedId)
    {
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var shown = Enumerable.Range(0, actors.Length)
            .Where(k => actors[k].Caption is not null && now - actors[k].CaptionSince is >= 0 and < CaptionFor &&
                !actors[k].Camouflaged && actors[k].OpacityAt(now) > .5f)
            .Select(k => (Actor: actors[k], Head: heads[k], X: (actors[k].X - camera.Left) * camera.Scale, Y: (heads[k] - camera.Top) * camera.Scale))
            .Where(a => a.X > -40 && a.X < screenWidth + 40 && a.Y > 0 && a.Y < screenHeight + 40)
            .OrderByDescending(a => a.Actor.Resident.Id == selectedId)
            .ThenByDescending(a => a.Actor.CaptionSince)
            .DistinctBy(a => a.Actor.Caption).Take(4).ToArray();
        ParkCaptionsPaint.Draw(canvas, shown.Select(a => new ParkCaptionsPaint.Caption(a.Actor.Caption!, a.X, a.Y - 24 * camera.Scale,
            ParkCaptionsPaint.Alpha(now - a.Actor.CaptionSince, CaptionFor))), screenWidth, PixelFont.For);
    }

    /// <summary>The name under the Pokémon you follow, in screen pixels.</summary>
    private static void DrawLabel(SKCanvas canvas, SKPaint paint, string text, float x, float y, int screenWidth, int screenHeight)
    {
        var name = text.Length > 15 ? text[..14] + "…" : text;
        var size = MathF.Max(13, screenWidth / 88f);
        using var font = PixelFont.For(name, size);
        var width = font.MeasureText(name, paint) + size;
        var height = size * 1.35f;
        var labelX = Math.Clamp(x, width / 2, screenWidth - width / 2);
        var top = Math.Min(screenHeight - height - 2, y + size * .5f);
        var filter = paint.ColorFilter;
        paint.ColorFilter = null;
        paint.IsAntialias = true;
        paint.Color = new SKColor(16, 22, 30, 165);
        canvas.DrawRoundRect(SKRect.Create(labelX - width / 2, top, width, height), height / 2, height / 2, paint);
        paint.Color = SKColors.White;
        canvas.DrawText(name, labelX, top + height * .76f, SKTextAlign.Center, font, paint);
        paint.IsAntialias = false;
        paint.ColorFilter = filter;
    }

    /// <summary>The resident under a point of the view, if any.</summary>
    public string? HitTest(float screenX, float screenY)
    {
        var point = _camera.ToMap(screenX, screenY);
        var closest = (string?)null;
        var distance = 20f * 20;
        foreach (var actor in Life?.Actors ?? [])
        {
            if (actor.Indoors || actor.Hidden || actor.InShadow || actor.Submerged) continue;
            var d = MathF.Pow(actor.X - point.X, 2) + MathF.Pow(actor.Y - actor.Altitude - 10 - point.Y, 2);
            if (d < distance) { distance = d; closest = actor.Resident.Id; }
        }
        return closest;
    }

    /// <summary>The map point under a point of the view.</summary>
    public ParkPoint ScreenToMap(float screenX, float screenY)
    {
        var point = _camera.ToMap(screenX, screenY);
        return new(point.X, point.Y);
    }

    private SKRect VisibleBounds(SKBitmap bitmap, SKRect cell)
    {
        // Copy the pixels once per sheet; avoid native GetPixel calls in the rendering loop.
        var cached = _visibleBounds.GetValue(bitmap, b => new FrameBounds(b.Pixels));
        var frames = cached.Frames;
        if (frames.TryGetValue(cell, out var bounds)) return bounds;
        var left = (int)cell.Right;
        var top = (int)cell.Bottom;
        var right = (int)cell.Left;
        var bottom = (int)cell.Top;
        for (var y = (int)cell.Top; y < (int)cell.Bottom; y++)
        for (var x = (int)cell.Left; x < (int)cell.Right; x++)
            if (cached.Pixels[y * bitmap.Width + x].Alpha > 0)
            {
                left = Math.Min(left, x); top = Math.Min(top, y);
                right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
            }
        bounds = right > left && bottom > top ? new SKRect(left, top, right, bottom) : cell;
        frames[cell] = bounds;
        return bounds;
    }
}
