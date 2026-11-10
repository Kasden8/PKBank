namespace PKForge.Domain;

/// <summary>A light a Pokémon gives off after dark: its colour (0xRRGGBB) and reach in art pixels.</summary>
public readonly record struct ParkGlow(uint Color, float Radius);

/// <summary>
/// What Pokémon can do, shown in the park. Every Ghost can fade out of sight and drift about
/// unseen before reappearing in a puff of violet smoke; Abra and the other teleporters blink
/// across the park in a flash; Kecleon vanishes into its surroundings; Ditto and Mew turn into
/// someone else for a while; Squirtle's line and the other water shooters spray whoever is near
/// (a Fire type hisses with steam, a Grass type loves it); the strong ones lift boulders over their
/// heads (and Machamp tries to lift a sleeping Snorlax); Geodude's line and the other round ones
/// roll about; Magikarp sometimes leaps astonishingly high; the sea giants raise a wave as they
/// surface; Porygon glitches; Torkoal and Koffing puff smoke and the neighbours cough; Butterfree,
/// Vileplume and Breloom scatter powder that sends the little ones nearby to sleep. Some powers are
/// always there: the Fire types, Ampharos, Lanturn, Volbeat and Umbreon glow after dark; Electric
/// types crackle when they are happy; Lucario turns towards anyone who comes near, its aura
/// flaring; Darkrai gives bad dreams to those asleep near it, until Cresselia comes. Some answer
/// the others: Gardevoir, Gallade and Mr. Mime raise a barrier for anyone being chased;
/// Wobbuffet counters a prank; Ninetales and Chandelure light will-o'-wisps when bothered; Mimikyu
/// will not have its disguise lifted.
/// </summary>
public sealed partial class ParkLife
{
    /// <summary>Fade to this opacity over that long (0 invisible, 1 as usual).</summary>
    internal sealed record Fade(float To, int Milliseconds) : Step;
    /// <summary>Blend into the surroundings, or come out of them.</summary>
    internal sealed record Camouflage(bool On) : Step;
    /// <summary>Gone from here, there at once (a teleport).</summary>
    internal sealed record Blink(ParkPoint To) : Step;
    /// <summary>A spray of water onto someone, and how they take it.</summary>
    internal sealed record SprayAt(string TargetId) : Step;
    /// <summary>A leap out of the water, this high, over that long.</summary>
    internal sealed record Leap(float Height, int Milliseconds) : Step;
    /// <summary>Those near enough cough at the smoke.</summary>
    internal sealed record Cough(float Radius) : Step;

    private enum Power { Fade, Teleport, Camouflage, Transform, WaterGun, Boulder, Rollout, BigLeap, Glitch, Smoke, Powder }

    private static readonly HashSet<int> Teleporters = [63, 64, 65, 151, 177, 178, 251, 280, 281, 282, 386, 720];
    private static readonly HashSet<int> WaterShooters = [7, 8, 9, 116, 117, 223, 224, 230, 60, 61, 62];
    private static readonly HashSet<int> Lifters = [66, 67, 68, 296, 297, 532, 533, 534, 760, 214];
    private static readonly HashSet<int> Rollers = [27, 28, 74, 75, 76, 206, 213, 231, 232, 241, 363, 364, 438, 524, 525, 526];
    private static readonly HashSet<int> Smokers = [109, 110, 324, 218, 219];
    private static readonly HashSet<int> Powderers = [12, 43, 44, 45, 182, 285, 286, 48, 49];
    private static readonly HashSet<int> Shielders = [282, 475, 122, 439, 866];
    private static readonly HashSet<int> Wisps = [37, 38, 607, 608, 609];

    private static IEnumerable<Power> PowersOf(ParkLifeResident resident)
    {
        var species = resident.Species;
        if (resident.Types.Contains(ParkType.Ghost) && species != 778) yield return Power.Fade;
        if (Teleporters.Contains(species)) yield return Power.Teleport;
        if (species == 352) yield return Power.Camouflage;
        if (species is 132 or 151) yield return Power.Transform;
        if (WaterShooters.Contains(species)) yield return Power.WaterGun;
        if (Lifters.Contains(species)) yield return Power.Boulder;
        if (Rollers.Contains(species)) yield return Power.Rollout;
        if (species is 129) yield return Power.BigLeap;
        if (species is 137 or 233 or 474) yield return Power.Glitch;
        if (Smokers.Contains(species)) yield return Power.Smoke;
        if (Powderers.Contains(species)) yield return Power.Powder;
    }

    private static bool HasPower(ParkLifeResident resident) => PowersOf(resident).Any();

    /// <summary>The light it gives off after dark, if any: the Fire types' flames, Ampharos's tail,
    /// Lanturn's and Chinchou's lures, Volbeat's and Illumise's tails, Umbreon's rings, the
    /// candle ghosts' violet fire.</summary>
    public static ParkGlow? GlowOf(ParkLifeResident resident) => resident.Species switch
    {
        607 or 608 or 609 => new(0xb070ff, 18),
        170 or 171 => new(0x80d0ff, 16),
        179 or 180 or 181 => new(0xfff080, 20),
        313 or 314 => new(0xd8ff70, 12),
        197 => new(0xffe070, 10),
        _ when resident.Types.Contains(ParkType.Fire) => new(0xffa040, 16),
        _ => null
    };

    private bool PlanPower(ParkActor actor)
    {
        var powers = PowersOf(actor.Resident).ToArray();
        if (powers.Length == 0) return false;
        var power = powers[actor.Random.Next(powers.Length)];
        var random = actor.Random;
        var name = actor.Resident.Name;
        switch (power)
        {
            case Power.Fade:
            {
                // A short drift, unseen, then back.
                var drift = FreePoint(actor.Navigator, random, actor, land: !actor.Resident.WaterBound) is { } p && Distance(p.X, p.Y, actor.X, actor.Y) < 110 ? p : (ParkPoint?)null;
                Add(actor, new Label("Fading out of sight", "Mischievous"), new Spark(ParkEffectKind.Smoke), new Fade(.12f, 900),
                    new Spotlight($"{name} vanished into thin air!", 1, 15_000), new Label("Drifting about unseen", "Mischievous"));
                if (drift is { } to) Add(actor, new GoTo(to, .9f));
                Add(actor, new Act(ParkPose.Idle, random.Next(3000, 7000)), new Fade(1, 700), new Spark(ParkEffectKind.Smoke),
                    new Label("Back, grinning", "Mischievous"), new Act(ParkPose.Hop, 1200, Emote: ParkEmote.Note));
                if (random.Next(4) == 0) Add(actor, new Journal($"{name} vanished into thin air and reappeared somewhere else, grinning."));
                return true;
            }
            case Power.Teleport:
            {
                var place = random.Next(3) == 0 ? null : HomePlace(actor.Resident);
                if (FreePoint(actor.Navigator, random, actor, land: true, place: place) is not { } to || Distance(to.X, to.Y, actor.X, actor.Y) < 60) return false;
                Add(actor, new Label("Teleporting", "Calm"), new Act(ParkPose.Charge, 900), new Spark(ParkEffectKind.Teleport), new Fade(0, 150),
                    new Act(ParkPose.Idle, 300), new Blink(to), new Spark(ParkEffectKind.Teleport), new Fade(1, 300),
                    new Act(ParkPose.Idle, 1500, Emote: random.Next(3) == 0 ? ParkEmote.Question : ParkEmote.None));
                if (random.Next(5) == 0) Add(actor, new Journal($"{name} teleported across the park, apparently because walking is for other Pokémon."));
                return true;
            }
            case Power.Camouflage:
            {
                if ((Spot(actor, ParkSpotKind.Nap) ?? Spot(actor, ParkSpotKind.Hide)) is not { } spot) return false;
                Add(actor, new Label("Blending in", "Mischievous"), new GoTo(new(spot.X, spot.Y)), new Camouflage(true),
                    new Spotlight($"{name} has blended into its surroundings. Can you find it?", 1, 20_000),
                    new Act(ParkPose.Idle, random.Next(15_000, 25_000)), new Camouflage(false), new Act(ParkPose.Hop, 1000, Emote: ParkEmote.Exclaim));
                return true;
            }
            case Power.Transform:
            {
                var model = _actors.Values.Where(o => o != actor && !o.Indoors && o.Resident.Species != actor.Resident.Species && !o.Resident.WaterBound &&
                    Distance(o.X, o.Y, actor.X, actor.Y) < 250).OrderBy(_ => random.Next()).FirstOrDefault();
                if (model is null) return false;
                Add(actor, new Label($"Turning into {model.Resident.Name}", "Mischievous"), new Face(model.Resident.Id), new Act(ParkPose.Charge, 900),
                    new Spark(ParkEffectKind.Psychic), new Copy(model.Resident.Id), new Label($"Being {model.Resident.Name} for a while", "Playful"),
                    new Act(ParkPose.Hop, 1200, Emote: ParkEmote.Note));
                if (FreePoint(actor.Navigator, random, actor, land: true) is { } wander) Add(actor, new GoTo(wander));
                if (random.Next(3) == 0) Add(actor, new Journal($"{name} spent the afternoon as {model.Resident.Name}. {model.Resident.Name} was not consulted.", model.Resident.Id));
                return true;
            }
            case Power.WaterGun:
            {
                var target = _actors.Values.Where(o => o != actor && Free(o) && o.Altitude == 0 && Distance(o.X, o.Y, actor.X, actor.Y) < 160 &&
                    actor.Navigator.Connected(new(actor.X, actor.Y), new(o.X, o.Y))).OrderBy(_ => random.Next()).FirstOrDefault();
                if (target is null || Beside(actor, target) is not { } spot) return false;
                Add(actor, new Label($"Taking aim at {target.Resident.Name}", "Mischievous"), new GoTo(spot), new Face(target.Resident.Id),
                    new Act(ParkPose.Attack, 900), new SprayAt(target.Resident.Id), new Act(ParkPose.Hop, 1200, Emote: ParkEmote.Note));
                return true;
            }
            case Power.Boulder:
            {
                var snorlax = _actors.Values.FirstOrDefault(o => o.Resident.Species == Snorlax && o.IsAsleep && !o.Indoors && !o.Busy &&
                    Distance(o.X, o.Y, actor.X, actor.Y) < 260 && actor.Navigator.Connected(new(actor.X, actor.Y), new(o.X, o.Y)));
                if (snorlax is not null && random.Next(2) == 0 && Beside(actor, snorlax) is { } next)
                {
                    Add(actor, new Label($"Trying to lift {snorlax.Resident.Name}", "Determined"), new GoTo(next), new Face(snorlax.Resident.Id),
                        new Act(ParkPose.Pull, 2500, Emote: ParkEmote.Sweat), new Spark(ParkEffectKind.Dirt), new Act(ParkPose.Pull, 2000, Emote: ParkEmote.Sweat),
                        new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Ellipsis),
                        new Journal($"{name} tried to lift {snorlax.Resident.Name}. {snorlax.Resident.Name} did not move, and did not wake up.", snorlax.Resident.Id));
                    return true;
                }
                var at = Spot(actor, ParkSpotKind.Gate) is { } gate ? new ParkPoint(gate.X, gate.Y) : FreePoint(actor.Navigator, random, actor, land: true);
                if (at is not { } there) return false;
                Add(actor, new Label("Looking for something heavy", "Determined"), new GoTo(there), new Act(ParkPose.Pull, 1300), new Spark(ParkEffectKind.Dirt),
                    new Label("Lifting a boulder over its head", "Proud"), new Act(ParkPose.Pose, 3000, Emote: ParkEmote.Exclaim, Prop: ParkProp.Boulder),
                    new Act(ParkPose.Idle, 1200, Prop: ParkProp.Boulder), new Spark(ParkEffectKind.Dirt), new Act(ParkPose.DeepBreath, 1500), new Gain(ParkNeed.Fun, 8));
                return true;
            }
            case Power.Rollout:
            {
                var points = Enumerable.Range(0, 3).Select(_ => FreePoint(actor.Navigator, random, actor, land: true)).OfType<ParkPoint>().ToArray();
                if (points.Length < 2) return false;
                Add(actor, new Label("Rolling about", "Joyful"), new Act(ParkPose.Spin, 600));
                foreach (var p in points) Add(actor, new GoTo(p, 2.4f, ParkPose.Spin), new Spark(ParkEffectKind.Dirt));
                Add(actor, new Act(ParkPose.Idle, 1200, Emote: ParkEmote.Sweat), new Gain(ParkNeed.Fun, 10));
                return true;
            }
            case Power.BigLeap:
            {
                if (!InWater(actor)) return false;
                var huge = random.Next(4) == 0;
                Add(actor, new Label(huge ? "Leaping astonishingly high" : "Leaping out of the water", "Determined"), new Act(ParkPose.Float, 800),
                    new Spark(ParkEffectKind.Splash), new Leap(huge ? 46 : 18, huge ? 1600 : 900), new Spark(ParkEffectKind.Wave),
                    new Act(ParkPose.Float, 1200, Emote: huge ? ParkEmote.Sparkle : ParkEmote.None));
                if (huge) Add(actor, new Spotlight($"{name} leapt higher than anyone expected!", 2, 8000, "The Great Leap", Secret: true),
                    new Journal($"{name} leapt higher than anyone expected. It did not seem to know why either."));
                return true;
            }
            case Power.Glitch:
                Add(actor, new Label("Glitching", "Confused"), new Fade(.3f, 60), new Fade(1, 60), new Act(ParkPose.Idle, 200), new Fade(.2f, 60),
                    new Spark(ParkEffectKind.Psychic), new Fade(1, 60), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Question));
                return true;
            case Power.Smoke:
                Add(actor, new Label("Puffing smoke", "Content"), new Act(ParkPose.Charge, 1200), new Spark(ParkEffectKind.Smoke), new Act(ParkPose.Idle, 600),
                    new Spark(ParkEffectKind.Smoke), new Cough(40), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Note));
                return true;
            case Power.Powder:
                Add(actor, new Label("Scattering powder", "Content"), new Act(ParkPose.Spin, 1400), new Spark(ParkEffectKind.Pollen), new Lull(36),
                    new Act(ParkPose.Idle, 1500));
                if (random.Next(4) == 0) Add(actor, new Journal($"{name} scattered powder about, and everyone nearby fell asleep where they stood."));
                return true;
        }
        return false;
    }

    private bool RunPowerStep(ParkActor actor, Step step, long now)
    {
        switch (step)
        {
            case Fade fade:
                actor.FadeFrom = actor.OpacityAt(now); actor.FadeTo = fade.To; actor.FadeSince = now; actor.FadeMs = fade.Milliseconds;
                return true;
            case Camouflage camo:
                actor.Camouflaged = camo.On;
                if (!camo.On) foreach (var other in Near(actor, 40).Where(o => !o.IsAsleep)) other.Say(ParkEmote.Exclaim, now);
                return true;
            case Blink blink:
                if (actor.Navigator.CanStand(blink.To.X, blink.To.Y)) { actor.X = blink.To.X; actor.Y = blink.To.Y; }
                return true;
            case SprayAt spray:
                if (_actors.TryGetValue(spray.TargetId, out var target))
                {
                    _effects.Add(new(ParkEffectKind.Spray, target.X, target.Y - 10, now));
                    var types = target.Resident.Types;
                    if (types.Contains(ParkType.Fire) && !types.Contains(ParkType.Water))
                    {
                        _effects.Add(new(ParkEffectKind.Smoke, target.X, target.Y - 8, now));
                        target.Say(ParkEmote.Anger, now);
                        if (actor.Random.Next(2) == 0)
                            _moments.Add(new ParkMoment(ParkSocialEventKind.Conflict, actor.Resident.Id, target.Resident.Id,
                                $"{actor.Resident.Name} sprayed {target.Resident.Name} with water. {target.Resident.Name} hissed with steam and has not calmed down."));
                    }
                    else target.Say(types.Contains(ParkType.Grass) || types.Contains(ParkType.Water) ? ParkEmote.Heart : ParkEmote.Sweat, now);
                }
                return true;

            case Cough cough:
                foreach (var other in Near(actor, cough.Radius).Where(o => !o.IsAsleep && o.Resident.Species != actor.Resident.Species)) other.Say(ParkEmote.Sweat, now);
                return true;
        }
        return false;
    }

    // The leap's arc: false while in the air (called straight from Run, see RunMuster).
    private static bool RunLeap(ParkActor actor, Leap leap, long now)
    {
        var left = actor.StepUntil - now;
        var t = 1 - Math.Clamp(left / (float)leap.Milliseconds, 0, 1);
        actor.Altitude = leap.Height * MathF.Sin(t * MathF.PI);
        actor.SetPose(ParkPose.Hop, now);
        if (left > 0) return false;
        actor.Altitude = 0;
        return true;
    }

    // ── Powers that are always there ─────────────────────────────────────────

    private long _nextNightmare;
    private DateOnly _nightmareTold;

    private void UpdatePowers(long now)
    {
        if (now % 500 >= TickMilliseconds) return;
        foreach (var actor in _actors.Values)
        {
            if (actor.IsAsleep || actor.Indoors || actor.Submerged) continue;
            var resident = actor.Resident;
            // Electric types crackle when they are happy.
            if (resident.Types.Contains(ParkType.Electric) && actor.ActivityMood is "Joyful" or "Delighted" or "Happy" or "Playful" or "Excited" &&
                actor.Random.Next(16) == 0)
                _effects.Add(new(ParkEffectKind.Crackle, actor.X, actor.Y - 10, now));
            // Lucario and Riolu sense whoever comes near and turn towards them, aura flaring.
            if (resident.Species is 447 or 448 && !actor.Busy && now >= actor.NextSense && actor.Current is Act { Pose: ParkPose.Idle or ParkPose.Sit })
            {
                var coming = _actors.Values.FirstOrDefault(o => o != actor && o.IsWalking && Distance(o.X, o.Y, actor.X, actor.Y) < 50 &&
                    o.Route is { } route && o.RouteLeg < route.Count &&
                    Distance(route[o.RouteLeg].X, route[o.RouteLeg].Y, actor.X, actor.Y) < Distance(o.X, o.Y, actor.X, actor.Y));
                if (coming is not null)
                {
                    actor.NextSense = now + 20_000;
                    actor.Facing = ParkMotion.Facing(coming.X - actor.X, coming.Y - actor.Y);
                    actor.Say(ParkEmote.Exclaim, now);
                    _effects.Add(new(ParkEffectKind.Aura, actor.X, actor.Y - 8, now));
                }
            }
        }
        // Darkrai brings bad dreams to those asleep near it; Cresselia comes to soothe them.
        if (Phase == ParkDayPhase.Night && now >= _nextNightmare)
        {
            _nextNightmare = now + 30_000;
            foreach (var darkrai in _actors.Values.Where(a => a.Resident.Species == 491 && !a.Indoors && !a.IsAsleep).ToArray())
            {
                var dreamers = Near(darkrai, 120).Where(o => o.IsAsleep && !o.Indoors).ToArray();
                foreach (var dreamer in dreamers)
                {
                    _effects.Add(new(ParkEffectKind.Nightmare, dreamer.X, dreamer.Y - 6, now));
                    dreamer.Say(ParkEmote.Sweat, now);
                    dreamer.Care = dreamer.Care.Add(ParkNeed.Energy, -3);
                }
                if (dreamers.Length == 0)
                {
                    // It goes looking for someone asleep out under the stars.
                    var sleeper = _actors.Values.Where(o => o.IsAsleep && !o.Indoors && o != darkrai && Distance(o.X, o.Y, darkrai.X, darkrai.Y) < 500 &&
                        darkrai.Navigator.Connected(new(darkrai.X, darkrai.Y), new(o.X, o.Y))).OrderBy(o => Distance(o.X, o.Y, darkrai.X, darkrai.Y)).FirstOrDefault();
                    if (sleeper is not null && !darkrai.Busy && Beside(darkrai, sleeper) is { } near)
                    {
                        Interrupt(darkrai);
                        Add(darkrai, new Label("Drifting among the sleepers", "Mischievous"), new GoTo(near, .6f), new Act(ParkPose.Idle, 20_000, Emote: ParkEmote.Ellipsis));
                    }
                    continue;
                }
                if (_nightmareTold != Today)
                {
                    _nightmareTold = Today;
                    _moments.Add(new ParkMoment(ParkSocialEventKind.Rest, darkrai.Resident.Id, "",
                        $"Everyone who slept near {darkrai.Resident.Name} had bad dreams tonight."));
                    Highlight(darkrai, $"{darkrai.Resident.Name} is giving bad dreams to the sleepers near it", 2, 30_000, "A Night of Nightmares");
                }
                if (_actors.Values.FirstOrDefault(c => c.Resident.Species == 488 && Free(c)) is { } cresselia && Beside(cresselia, dreamers[0]) is { } bedside)
                {
                    Interrupt(cresselia);
                    Add(cresselia, new Label("Soothing the bad dreams", "Caring"), new GoTo(bedside, 1.2f), new Act(ParkPose.Dance, 3000, Emote: ParkEmote.Sparkle),
                        new Spark(ParkEffectKind.Hearts),
                        new Journal($"{cresselia.Resident.Name} came in the night and sang the bad dreams away. {darkrai.Resident.Name} watched from the dark.", darkrai.Resident.Id));
                }
            }
        }
    }

    // ── Powers in answer to others ───────────────────────────────────────────

    // Someone is being chased: a Pokémon that can raise a barrier steps between them.
    private void RaiseBarrier(ParkActor chased, ParkActor threat)
    {
        var guard = _actors.Values.Where(g => Shielders.Contains(g.Resident.Species) && g != chased && g != threat && Free(g) &&
                Distance(g.X, g.Y, chased.X, chased.Y) < 160 && g.Navigator.Connected(new(g.X, g.Y), new(chased.X, chased.Y)))
            .OrderBy(g => Distance(g.X, g.Y, chased.X, chased.Y)).FirstOrDefault();
        if (guard is null || guard.Random.Next(2) == 0) return;
        var between = new ParkPoint((chased.X + threat.X) / 2, (chased.Y + threat.Y) / 2);
        if (!guard.Navigator.CanStand(between.X, between.Y)) between = new ParkPoint(chased.X + 12, chased.Y);
        if (!guard.Navigator.CanStand(between.X, between.Y)) return;
        Interrupt(guard);
        Add(guard, new Label($"Protecting {chased.Resident.Name}", "Determined"), new GoTo(between, 1.8f), new Face(threat.Resident.Id),
            new Act(ParkPose.Charge, 600), new Spark(ParkEffectKind.Barrier), new Act(ParkPose.Pose, 2500, Emote: ParkEmote.Exclaim),
            new Spotlight($"{guard.Resident.Name} raised a barrier to protect {chased.Resident.Name}!", 2, 10_000),
            new Journal($"{threat.Resident.Name} went after {chased.Resident.Name}, and {guard.Resident.Name} raised a barrier between them. {threat.Resident.Name} thought better of it.",
                chased.Resident.Id, ParkSocialEventKind.Help));
        if (!threat.Busy) { Interrupt(threat); Add(threat, new Act(ParkPose.Cringe, 900, Emote: ParkEmote.Exclaim), new Act(ParkPose.Idle, 1500, Emote: ParkEmote.Ellipsis)); }
    }

    // Bothered, Ninetales and the candle ghosts light will-o'-wisps around themselves.
    private void WillOWisp(ParkActor actor)
    {
        if (!Wisps.Contains(actor.Resident.Species)) return;
        _effects.Add(new(ParkEffectKind.Burn, actor.X - 8, actor.Y - 8, Now));
        _effects.Add(new(ParkEffectKind.Burn, actor.X + 8, actor.Y - 8, Now));
        _effects.Add(new(ParkEffectKind.Smoke, actor.X, actor.Y - 4, Now));
    }
}
