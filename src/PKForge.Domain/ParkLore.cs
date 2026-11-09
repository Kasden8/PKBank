namespace PKForge.Domain;

/// <summary>How a resident takes to people and to the others.</summary>
public enum ParkTemperament { Gentle, Friendly, Playful, Aloof, Feisty, Fierce, Timid, Lazy, Mischievous }

/// <summary>What a pair of species feels for each other, whatever the park's history says.</summary>
public enum ParkPairKind { None, Love, Family, Friends, Rivals, Fears, Hunts }

/// <summary>What goes wrong when you touch a Pokémon that cannot help being what it is.</summary>
public enum ParkAccident { None, Zap, Burn, Frost, Poison, Ghost, Splash, Pollen, Sting, Bite, Lick, Psychic }

/// <summary>Species habits that change what a resident does in the park.</summary>
[Flags]
public enum ParkQuirk
{
    None = 0, Nocturnal = 1, Healer = 2, Singer = 4, MoonDancer = 8, Sleepyhead = 16, Glutton = 32, Prankster = 64,
    Splasher = 128, Slow = 256, DeviceCurious = 512, Loner = 1024, Headache = 2048, CoinFinder = 4096, PretendTree = 8192,
    Copycat = 16384, Licker = 32768, EggGiver = 65536, Lullaby = 131072
}

/// <summary>
/// The park's knowledge of Pokémon, written by hand: temperaments by species, pairs that love,
/// fear or hate each other, the accidents of touching one, and species habits with their own
/// lines. Everything here is data; <see cref="ParkLife"/> acts on it.
/// </summary>
public static class ParkLore
{
    private static HashSet<int> Set(params int[] species) => [.. species];

    private static readonly HashSet<int> Fierce = Set(24, 34, 57, 112, 115, 128, 130, 217, 229, 248, 262, 303, 319, 335, 336, 373, 445, 452,
        461, 491, 553, 560, 612, 625, 635, 675, 717, 799, 861, 862, 979, 983, 382, 383, 384, 487);
    private static readonly HashSet<int> Aloof = Set(38, 53, 144, 145, 146, 150, 196, 197, 243, 244, 245, 249, 250, 254, 359, 377, 378, 379,
        380, 381, 385, 386, 448, 480, 481, 482, 483, 484, 485, 486, 488, 571, 637, 638, 639, 640, 641, 642, 643, 644, 645, 646, 647,
        716, 718, 785, 786, 787, 788, 791, 792, 800, 888, 889, 890, 892, 894, 895, 896, 897, 898, 1001, 1002, 1003, 1004, 1007, 1008);
    private static readonly HashSet<int> Feisty = Set(4, 5, 6, 7, 8, 9, 56, 66, 67, 68, 98, 99, 106, 107, 158, 159, 160, 214, 236, 237, 255, 256,
        257, 258, 390, 391, 392, 447, 453, 454, 559, 674, 677, 678, 701, 852, 853, 909, 910, 911);
    private static readonly HashSet<int> Playful = Set(25, 26, 39, 133, 172, 174, 175, 183, 216, 298, 311, 312, 417, 427, 506, 587, 702, 744, 777,
        835, 877, 921, 926, 58, 209, 190, 424);
    private static readonly HashSet<int> Timid = Set(63, 64, 104, 155, 179, 280, 281, 360, 438, 549, 595, 704, 742, 778, 816, 840, 872, 885);
    private static readonly HashSet<int> Lazy = Set(54, 79, 80, 143, 194, 195, 199, 202, 213, 287, 289, 363, 399, 446, 775);
    private static readonly HashSet<int> Mischievous = Set(52, 92, 93, 94, 198, 200, 215, 302, 353, 354, 355, 425, 426, 429, 479, 509, 570, 708,
        710, 720, 827, 859, 860);
    private static readonly HashSet<int> Gentle = Set(1, 2, 3, 113, 131, 152, 153, 154, 242, 282, 334, 350, 440, 468, 531, 594, 764, 856, 857,
        858, 876, 40, 36, 35, 173, 765);

    public static ParkTemperament TemperamentOf(int species, ParkPersonality personality)
    {
        if (Fierce.Contains(species)) return ParkTemperament.Fierce;
        if (Aloof.Contains(species)) return ParkTemperament.Aloof;
        if (Mischievous.Contains(species)) return ParkTemperament.Mischievous;
        if (Lazy.Contains(species)) return ParkTemperament.Lazy;
        if (Timid.Contains(species)) return ParkTemperament.Timid;
        if (Gentle.Contains(species)) return ParkTemperament.Gentle;
        if (Feisty.Contains(species)) return ParkTemperament.Feisty;
        if (Playful.Contains(species)) return ParkTemperament.Playful;
        return personality switch
        {
            ParkPersonality.Bold => ParkTemperament.Feisty,
            ParkPersonality.Proud => ParkTemperament.Aloof,
            ParkPersonality.Shy => ParkTemperament.Timid,
            ParkPersonality.Playful or ParkPersonality.Energetic => ParkTemperament.Playful,
            ParkPersonality.Gentle or ParkPersonality.Helpful or ParkPersonality.Calm => ParkTemperament.Gentle,
            _ => ParkTemperament.Friendly
        };
    }

    /// <summary>How readily it goes along with the player, 0 to 1: its temperament, raised by the
    /// bond, lowered when it is grumpy, hungry or sleepy, nudged by its nature.</summary>
    public static float Docility(ParkTemperament temperament, float bond, string mood, int? nature)
    {
        var score = temperament switch
        {
            ParkTemperament.Gentle => .9f, ParkTemperament.Friendly => .8f, ParkTemperament.Playful => .8f,
            ParkTemperament.Timid => .55f, ParkTemperament.Lazy => .5f, ParkTemperament.Mischievous => .5f,
            ParkTemperament.Feisty => .45f, ParkTemperament.Aloof => .3f, _ => .15f
        };
        score += bond / 100 * .55f;
        if (mood is "Grumpy" or "Hungry" or "Sleepy") score -= .25f;
        // Natures that raise Attack (Lonely, Brave, Adamant, Naughty) bristle; the calm ones settle.
        if (nature is >= 1 and <= 4) score -= .1f;
        if (nature is 20 or 21 or 22 or 23) score += .08f;
        return Math.Clamp(score, .03f, 1);
    }

    /// <summary>What a temperament means for the player, in a line.</summary>
    public static string Explain(ParkTemperament temperament) => temperament switch
    {
        ParkTemperament.Gentle => "Gentle: goes along with nearly anything, and looks sorry when it zaps you by accident.",
        ParkTemperament.Friendly => "Friendly: happy to see you, happy to play.",
        ParkTemperament.Playful => "Playful: always up for a game, and runs after balls nobody else wants.",
        ParkTemperament.Aloof => "Aloof: often ignores you and lets the ball roll away. A strong bond changes that.",
        ParkTemperament.Feisty => "Feisty: quick to huff and stomp off, but loves a race.",
        ParkTemperament.Fierce => "Fierce: often in no mood, may snap, fights others. Patience and a strong bond calm it.",
        ParkTemperament.Timid => "Timid: hides when rushed, hides best at hide and seek, is easily bullied.",
        ParkTemperament.Lazy => "Lazy: may not even open its eyes for you, and lies down in the middle of races.",
        _ => "Mischievous: teases you, steals the ball, and makes trouble for the others."
    };

    /// <summary>Its habits, each with what it means.</summary>
    public static IReadOnlyList<(string Habit, string Meaning)> Habits(int species, IReadOnlyList<int> types, bool troll)
    {
        var quirks = QuirksOf(species, types);
        var list = new List<(string, string)>();
        void When(ParkQuirk quirk, string habit, string meaning) { if ((quirks & quirk) != 0) list.Add((habit, meaning)); }
        if (troll) list.Add(("Troublemaker", "steals berries, wakes sleepers, trips others; someone may stand up to it"));
        When(ParkQuirk.Prankster, "Prankster", "sneaks up on others to scare them");
        When(ParkQuirk.Nocturnal, "Night owl", "up all night, naps during the day");
        When(ParkQuirk.Healer, "Healer", "checks on residents who are low and cheers them up");
        When(ParkQuirk.Lullaby, "Lullaby", "its song puts everyone nearby to sleep, which makes it furious");
        When(ParkQuirk.Singer, "Singer", "sings for the park and lifts everyone's mood");
        When(ParkQuirk.MoonDancer, "Moon dancer", "dances by the statue at night");
        When(ParkQuirk.Sleepyhead, "Sleepyhead", "naps far more than the others");
        When(ParkQuirk.Glutton, "Big eater", "gets hungry twice as fast");
        When(ParkQuirk.Splasher, "Splash", "splashes around to no effect whatsoever");
        When(ParkQuirk.Slow, "Slow", "moves at half speed and answers much later");
        When(ParkQuirk.DeviceCurious, "Tech-curious", "very interested in your device");
        When(ParkQuirk.Loner, "Loner", "keeps to the quietest corner it can find; lets one friend sit with it");
        When(ParkQuirk.Headache, "Headache", "holds its head; sometimes a psychic wave escapes");
        When(ParkQuirk.CoinFinder, "Treasure nose", "digs up coins, and digs better for you");
        When(ParkQuirk.PretendTree, "Tree", "pretends to be a tree. Nobody is fooled");
        When(ParkQuirk.Copycat, "Copycat", "turns into whoever it greets");
        When(ParkQuirk.EggGiver, "Gifts", "brings you an egg once a day when you are close");
        if (types.Contains(ParkType.Fighting)) list.Add(("Martial artist", "trains you: Play, then Train"));
        return list;
    }

    /// <summary>What touching it might do, or null when nothing much.</summary>
    public static string? TouchWarning(int species, IReadOnlyList<int> types, ParkTemperament temperament) =>
        AccidentOf(species, types, temperament, 0, "Happy").Kind switch
        {
            ParkAccident.Zap => "may zap you when you pat it",
            ParkAccident.Burn => "runs hot: may singe your fingers",
            ParkAccident.Frost => "may frost your fingers",
            ParkAccident.Poison => "your hand will smell after a pat",
            ParkAccident.Ghost => "your hand may go right through it",
            ParkAccident.Splash => "may shake itself off all over you",
            ParkAccident.Pollen => "may make you sneeze",
            ParkAccident.Sting => "prickly when startled",
            ParkAccident.Bite when temperament == ParkTemperament.Fierce => "may snap at your hand",
            ParkAccident.Lick => "will lick you. A lot",
            ParkAccident.Psychic => "reads your mind when you pat it",
            _ => null
        };

    // ── Pairs ────────────────────────────────────────────────────────────────

    private static readonly (int[] A, int[] B, ParkPairKind Kind)[] Pairs =
    [
        ([313], [314], ParkPairKind.Love),                       // Volbeat and Illumise
        ([29, 30, 31], [32, 33, 34], ParkPairKind.Love),         // the Nidoran lines
        ([311], [312], ParkPairKind.Love),                       // Plusle and Minun
        ([380], [381], ParkPairKind.Family),                     // Latias and Latios
        ([282], [475], ParkPairKind.Love),                       // Gardevoir and Gallade
        ([128], [241], ParkPairKind.Love),                       // Tauros and Miltank
        ([370], [370], ParkPairKind.Love),                       // two Luvdisc
        ([413], [414], ParkPairKind.Love),                       // Wormadam and Mothim
        ([172, 25, 26], [172, 25, 26], ParkPairKind.Family),     // the Pikachu line
        ([133, 134, 135, 136, 196, 197, 470, 471, 700], [133, 134, 135, 136, 196, 197, 470, 471, 700], ParkPairKind.Family), // Eevee and its evolutions
        ([440, 113, 242], [440, 113, 242], ParkPairKind.Family), // Happiny, Chansey, Blissey
        ([175, 176, 468], [175, 176, 468], ParkPairKind.Family), // Togepi's line
        ([104, 105], [115], ParkPairKind.Family),                // Cubone finds comfort with Kangaskhan
        ([888], [889], ParkPairKind.Family),                     // Zacian and Zamazenta
        ([335], [336], ParkPairKind.Rivals),                     // Zangoose and Seviper, sworn enemies
        ([106], [107], ParkPairKind.Rivals),                     // Hitmonlee and Hitmonchan
        ([538], [539], ParkPairKind.Rivals),                     // Throh and Sawk
        ([382], [383], ParkPairKind.Rivals),                     // Kyogre and Groudon
        ([483], [484], ParkPairKind.Rivals),                     // Dialga and Palkia
        ([643], [644], ParkPairKind.Rivals),                     // Reshiram and Zekrom
        ([249], [250], ParkPairKind.Rivals),                     // Lugia and Ho-Oh
        ([631], [632], ParkPairKind.Hunts),                      // Heatmor hunts Durant
        ([302], [703, 719], ParkPairKind.Hunts),                 // Sableye eyes Carbink and Diancie
        ([24, 23], [16, 21], ParkPairKind.Hunts),                // Ekans and Arbok stalk Pidgey and Spearow
        ([125, 466, 239], [126, 467, 240], ParkPairKind.Rivals), // Electabuzz and Magmar, at every stage
        ([127], [214], ParkPairKind.Rivals),                     // Pinsir and Heracross
        ([491], [488], ParkPairKind.Rivals),                     // Darkrai and Cresselia: nightmares and dreams
        ([716], [717], ParkPairKind.Rivals),                     // Xerneas and Yveltal
        ([144], [145], ParkPairKind.Rivals), ([145], [146], ParkPairKind.Rivals), ([144], [146], ParkPairKind.Rivals), // the legendary birds
        ([1007], [1008], ParkPairKind.Rivals),                   // Koraidon and Miraidon
        ([150], [151], ParkPairKind.Rivals),                     // Mewtwo and Mew
        ([1017], [1014, 1015, 1016], ParkPairKind.Rivals),       // Ogerpon and the Loyal Three
        ([641, 642], [645], ParkPairKind.Rivals),                // Landorus keeps Tornadus and Thundurus in line
        ([641], [642], ParkPairKind.Rivals),
        ([243, 244, 245, 250], [243, 244, 245, 250], ParkPairKind.Family), // the legendary beasts and Ho-Oh
        ([377, 378, 379, 486, 894, 895], [377, 378, 379, 486, 894, 895], ParkPairKind.Family), // the Regis
        ([480, 481, 482], [480, 481, 482], ParkPairKind.Family), // the lake trio
        ([638, 639, 640, 647], [638, 639, 640, 647], ParkPairKind.Family), // the Swords of Justice
        ([337], [338], ParkPairKind.Family),                     // Lunatone and Solrock
        ([789, 790, 791, 792], [789, 790, 791, 792], ParkPairKind.Family), // Cosmog's line
        ([143], [446], ParkPairKind.Family),                     // Snorlax and Munchlax
        ([129], [130], ParkPairKind.Family),                     // Magikarp and Gyarados
        ([415], [416], ParkPairKind.Family),                     // Combee and Vespiquen
        ([667], [668], ParkPairKind.Family),                     // Litleo and Pyroar
        ([298, 183, 184], [298, 183, 184], ParkPairKind.Family), // Azurill's line
        ([1, 2, 3, 4, 5, 6, 7, 8, 9], [1, 2, 3, 4, 5, 6, 7, 8, 9], ParkPairKind.Friends),             // the Kanto starters
        ([152, 153, 154, 155, 156, 157, 158, 159, 160], [152, 153, 154, 155, 156, 157, 158, 159, 160], ParkPairKind.Friends),
        ([252, 253, 254, 255, 256, 257, 258, 259, 260], [252, 253, 254, 255, 256, 257, 258, 259, 260], ParkPairKind.Friends),
        ([387, 388, 389, 390, 391, 392, 393, 394, 395], [387, 388, 389, 390, 391, 392, 393, 394, 395], ParkPairKind.Friends),
        ([495, 496, 497, 498, 499, 500, 501, 502, 503], [495, 496, 497, 498, 499, 500, 501, 502, 503], ParkPairKind.Friends),
        ([650, 651, 652, 653, 654, 655, 656, 657, 658], [650, 651, 652, 653, 654, 655, 656, 657, 658], ParkPairKind.Friends),
        ([722, 723, 724, 725, 726, 727, 728, 729, 730], [722, 723, 724, 725, 726, 727, 728, 729, 730], ParkPairKind.Friends),
        ([810, 811, 812, 813, 814, 815, 816, 817, 818], [810, 811, 812, 813, 814, 815, 816, 817, 818], ParkPairKind.Friends),
        ([906, 907, 908, 909, 910, 911, 912, 913, 914], [906, 907, 908, 909, 910, 911, 912, 913, 914], ParkPairKind.Friends),
        ([79, 80, 199], [90], ParkPairKind.Friends),             // Slowpoke and the Shellder on its tail
        ([223], [226, 458], ParkPairKind.Friends),               // Remoraid rides with Mantine
        ([94], [36], ParkPairKind.Friends),                      // Gengar, said to be Clefable's shadow
        ([25], [393], ParkPairKind.Friends),                     // Pikachu and Piplup
        ([780], [172, 173, 174, 175, 236, 238, 239, 240, 298, 360, 406, 433, 438, 439, 440, 446, 447, 458, 848], ParkPairKind.Family), // Drampa minds the babies
        ([16, 17, 18, 396, 397, 398, 661, 662, 663, 821, 822, 823], [10, 11, 13, 14, 265, 266, 268, 664, 736, 824], ParkPairKind.Hunts), // birds and caterpillars
        ([747, 748], [222, 864], ParkPairKind.Hunts),            // Mareanie feeds on Corsola
    ];

    // Pikachu look-alikes: Mimikyu cannot stand them, or is scared of them.
    private static readonly HashSet<int> PikachuLike = Set(25, 26, 172, 417, 587, 702, 777, 877, 921, 922, 923);

    /// <summary>How <paramref name="a"/> feels about <paramref name="b"/> (not always mutual: Durant fears
    /// Heatmor, which hunts it). <paramref name="seed"/> picks Mimikyu's mood toward Pikachu for a given pair.</summary>
    public static ParkPairKind Feeling(int a, int b, int seed = 0)
    {
        if (a == 778 && PikachuLike.Contains(b)) return seed % 2 == 0 ? ParkPairKind.Rivals : ParkPairKind.Fears;
        if (b == 778 && PikachuLike.Contains(a)) return ParkPairKind.None;
        foreach (var (left, right, kind) in Pairs)
        {
            if (left.Contains(a) && right.Contains(b)) return kind;
            if (right.Contains(a) && left.Contains(b))
                return kind == ParkPairKind.Hunts ? ParkPairKind.Fears : kind;
        }
        return ParkPairKind.None;
    }

    // ── Accidents ────────────────────────────────────────────────────────────

    /// <summary>What a touch may set off, from its types (the first that applies), and how likely it
    /// is for this resident: less with a strong bond, more when it is in a bad mood.</summary>
    public static (ParkAccident Kind, float Chance) AccidentOf(int species, IReadOnlyList<int> types, ParkTemperament temperament, float bond, string mood)
    {
        var (kind, chance) = species switch
        {
            108 or 463 => (ParkAccident.Lick, .8f),
            _ when types.Contains(ParkType.Electric) => (ParkAccident.Zap, .45f),
            _ when types.Contains(ParkType.Fire) => (ParkAccident.Burn, .3f),
            _ when types.Contains(ParkType.Ghost) => (ParkAccident.Ghost, .35f),
            _ when types.Contains(ParkType.Ice) => (ParkAccident.Frost, .3f),
            _ when types.Contains(ParkType.Poison) => (ParkAccident.Poison, .3f),
            _ when types.Contains(ParkType.Psychic) => (ParkAccident.Psychic, .2f),
            _ when types.Contains(ParkType.Water) => (ParkAccident.Splash, .2f),
            _ when types.Contains(ParkType.Grass) => (ParkAccident.Pollen, .18f),
            _ when types.Contains(ParkType.Bug) => (ParkAccident.Sting, .12f),
            _ => (ParkAccident.Bite, .05f)
        };
        if (temperament == ParkTemperament.Fierce) { kind = kind == ParkAccident.Lick ? kind : ParkAccident.Bite; chance += .25f; }
        if (temperament == ParkTemperament.Gentle) chance *= .5f;
        if (mood is "Grumpy" or "Sleepy" or "Hungry") chance += .15f;
        chance *= 1 - Math.Clamp(bond, 0, 100) / 160;
        return (kind, Math.Clamp(chance, 0, .9f));
    }

    /// <summary>What happens to your hand, in a line.</summary>
    public static string AccidentLine(ParkAccident accident, string name) => accident switch
    {
        ParkAccident.Zap => $"{name}'s cheeks crackle the moment you touch it. Zap! Your hand tingles for a good while. It did not mean to.",
        ParkAccident.Burn => $"Ouch! {name} runs a little too warm today. You blow on your fingers while it looks very sorry.",
        ParkAccident.Frost => $"Your fingers come back covered in frost. {name} tilts its head, wondering why you are shivering.",
        ParkAccident.Poison => $"{name} leans happily into your hand. Your hand will smell funny for the rest of the day.",
        ParkAccident.Ghost => $"Your hand goes right through {name}. A chill runs up your arm, and {name} giggles.",
        ParkAccident.Splash => $"{name} shakes itself off right next to you. You are soaked.",
        ParkAccident.Pollen => $"A puff of pollen rises from {name}. You sneeze three times in a row.",
        ParkAccident.Sting => $"{name} startles and its spines prickle your palm. Not hard, but enough.",
        ParkAccident.Bite => $"{name} snaps at your hand! It missed, on purpose. Probably.",
        ParkAccident.Lick => $"{name} licks your whole hand, then your arm. Thank you, {name}.",
        ParkAccident.Psychic => $"{name} stares into your eyes as you pat it. For a moment you are sure it knows what you had for breakfast.",
        _ => ""
    };

    // ── Quirks ───────────────────────────────────────────────────────────────

    private static readonly Dictionary<int, ParkQuirk> Quirks = new()
    {
        [39] = ParkQuirk.Lullaby, [40] = ParkQuirk.Singer, [174] = ParkQuirk.Lullaby,
        [131] = ParkQuirk.Singer, [730] = ParkQuirk.Singer, [351] = ParkQuirk.Singer,
        [35] = ParkQuirk.MoonDancer, [36] = ParkQuirk.MoonDancer, [173] = ParkQuirk.MoonDancer,
        [113] = ParkQuirk.Healer | ParkQuirk.EggGiver, [242] = ParkQuirk.Healer | ParkQuirk.EggGiver, [440] = ParkQuirk.Healer,
        [531] = ParkQuirk.Healer, [764] = ParkQuirk.Healer, [594] = ParkQuirk.Healer, [856] = ParkQuirk.Healer, [857] = ParkQuirk.Healer, [858] = ParkQuirk.Healer,
        [143] = ParkQuirk.Sleepyhead | ParkQuirk.Glutton, [446] = ParkQuirk.Glutton, [775] = ParkQuirk.Sleepyhead,
        [287] = ParkQuirk.Sleepyhead, [289] = ParkQuirk.Sleepyhead,
        [92] = ParkQuirk.Prankster, [93] = ParkQuirk.Prankster | ParkQuirk.Licker, [94] = ParkQuirk.Prankster, [302] = ParkQuirk.Prankster,
        [570] = ParkQuirk.Prankster, [571] = ParkQuirk.Prankster, [859] = ParkQuirk.Prankster, [860] = ParkQuirk.Prankster, [509] = ParkQuirk.Prankster,
        [129] = ParkQuirk.Splasher,
        [79] = ParkQuirk.Slow, [80] = ParkQuirk.Slow, [199] = ParkQuirk.Slow, [213] = ParkQuirk.Slow,
        [137] = ParkQuirk.DeviceCurious, [233] = ParkQuirk.DeviceCurious, [474] = ParkQuirk.DeviceCurious, [479] = ParkQuirk.DeviceCurious,
        [104] = ParkQuirk.Loner, [778] = ParkQuirk.Loner, [359] = ParkQuirk.Loner, [150] = ParkQuirk.Loner, [442] = ParkQuirk.Loner,
        [491] = ParkQuirk.Loner, [197] = ParkQuirk.Loner,
        [54] = ParkQuirk.Headache, [52] = ParkQuirk.CoinFinder, [53] = ParkQuirk.CoinFinder, [863] = ParkQuirk.CoinFinder,
        [185] = ParkQuirk.PretendTree, [438] = ParkQuirk.PretendTree, [132] = ParkQuirk.Copycat,
        [108] = ParkQuirk.Licker, [463] = ParkQuirk.Licker,
    };

    private static readonly HashSet<int> NightOwls = Set(41, 42, 163, 164, 169, 197, 198, 228, 229, 261, 262, 430, 488, 491, 714, 715);

    public static ParkQuirk QuirksOf(int species, IReadOnlyList<int> types)
    {
        var quirks = Quirks.GetValueOrDefault(species);
        if (types.Contains(ParkType.Ghost) || NightOwls.Contains(species)) quirks |= ParkQuirk.Nocturnal;
        return quirks;
    }

    /// <summary>A species' own line when you talk to it, if it has one (picked by <paramref name="roll"/>).</summary>
    public static string? TalkLine(int species, string name, int roll) => species switch
    {
        137 or 233 or 474 => Pick(roll,
            $"{name} lightly taps your electronic device out of curiosity. You wonder whether it wants to play with it or go inside it.",
            $"{name} hovers close to your screen, reading the menus with great interest."),
        479 => $"{name} eyes your device a little too long. You hold on to it, just in case.",
        54 => Pick(roll, $"{name} holds its head and stares at you blankly. Its headache is back.",
            $"{name} tilts its head the other way. Then the first way again. Then it sits down."),
        79 or 80 or 199 => $"{name} stares at you. ... ... ... It waves, much later, once you had stopped waiting.",
        129 => $"{name} used Splash! But nothing happened.",
        104 => Pick(roll, $"{name} hugs its bone and looks at the sky. It seems to be thinking of someone.",
            $"{name} lets you sit next to it. It does not say anything, but it does not leave either."),
        778 => Pick(roll, $"{name} turns away so you cannot see under its disguise. It adjusts the drawn face, just in case.",
            $"{name} stays very still, pretending to be a Pikachu. You decide to play along."),
        143 => Pick(roll, $"{name} opens one eye, checks whether you brought food, and closes it again.",
            $"{name}'s stomach rumbles louder than its voice."),
        185 or 438 => $"{name} stands perfectly still with its arms out. It is a tree. It is very clearly a tree.",
        132 => $"{name} copies your face. It gets the nose a little wrong.",
        52 or 53 => $"{name} looks at your pockets, then at you, then back at your pockets.",
        202 => $"{name}! {name} salutes you very seriously.",
        25 => Pick(roll, $"{name} hops onto your shoulder for a second. You feel its cheeks fizz against your ear.",
            $"{name} sniffs your bag for a Poké Ball and makes it clear it is not getting in one."),
        39 => $"{name} takes a deep breath and gets ready to sing. You might want to stay awake for this one.",
        150 => $"{name} looks at you for a long moment. A thought that is not yours says: \"Not now.\"",
        94 => $"{name} grins from your shadow. You were sure it was in front of you a second ago.",
        122 => $"{name} presses its hands against an invisible wall between you, then pretends to look for a door.",
        175 => $"{name} giggles at nothing in particular, and somehow everyone nearby feels a little better.",
        _ => null
    };

    private static string Pick(int roll, params string[] lines) => lines[Math.Abs(roll) % lines.Length];

    // The aloof, the lazy and the mischievous each turn a game down in their own way.
    private static string? GameRefusal(ParkTemperament temperament, string name, ParkGameKind game) => (temperament, game) switch
    {
        (ParkTemperament.Aloof, ParkGameKind.HideAndSeek) => $"{name} looks at you as if hide and seek were beneath it.",
        (ParkTemperament.Aloof, ParkGameKind.Race) => $"{name} watches the starting line from a distance. Racing is for others.",
        (ParkTemperament.Aloof, ParkGameKind.Brush) => $"{name} steps away from the brush. It grooms itself, thank you.",
        (ParkTemperament.Aloof, ParkGameKind.Dig) => $"{name} glances at the spot, then at you. It does not dig.",
        (ParkTemperament.Aloof, ParkGameKind.Spar) => $"{name} does not even raise a paw. You are not worth the effort today.",
        (ParkTemperament.Lazy, ParkGameKind.HideAndSeek) => $"{name} closes its eyes. It is hiding, it says. Right where it is.",
        (ParkTemperament.Lazy, ParkGameKind.Race) => $"{name} looks down the track, yawns, and lies back down.",
        (ParkTemperament.Lazy, ParkGameKind.Brush) => $"{name} falls asleep before the brush even touches it.",
        (ParkTemperament.Lazy, ParkGameKind.Dig) => $"{name} scratches the ground once and decides that is enough digging for today.",
        (ParkTemperament.Lazy, ParkGameKind.Spar) => $"{name} yawns at your fists. Maybe tomorrow.",
        (ParkTemperament.Mischievous, ParkGameKind.HideAndSeek) => $"{name} runs off to hide, and never comes back to play. That was the game, apparently.",
        (ParkTemperament.Mischievous, ParkGameKind.Race) => $"{name} sets off before the start, laughs, and stops halfway.",
        (ParkTemperament.Mischievous, ParkGameKind.Brush) => $"{name} grabs the brush and runs off with it.",
        (ParkTemperament.Mischievous, ParkGameKind.Dig) => $"{name} digs, finds something, and buries it again before you can see it.",
        (ParkTemperament.Mischievous, ParkGameKind.Spar) => $"{name} bows, then pokes you and runs away.",
        _ => null
    };

    /// <summary>The line when it will not go along with you, by temperament, and by game when it was
    /// asked to play one (so a refused brushing never talks about a ball).</summary>
    public static string RefusalLine(ParkTemperament temperament, string name, ParkCommand command, int roll, ParkGameKind? game = null) =>
        game is { } kind && GameRefusal(temperament, name, kind) is { } line ? line : (temperament, command) switch
    {
        (ParkTemperament.Fierce, _) => Pick(roll, $"{name} growls low. You decide to give it some space.",
            $"{name} bares its teeth without even looking at you. Not today."),
        (ParkTemperament.Aloof, ParkCommand.Play) => $"{name} watches the ball roll away. It could not care less.",
        (ParkTemperament.Aloof, _) => Pick(roll, $"{name} glances at you, then looks away as if you were not there.",
            $"{name} closes its eyes. You have been dismissed."),
        (ParkTemperament.Lazy, ParkCommand.Play) => $"{name} watches the ball, yawns, and decides it is too far.",
        (ParkTemperament.Lazy, _) => $"{name} does not move. Maybe it will answer tomorrow.",
        (ParkTemperament.Timid, _) => $"{name} hides behind the nearest thing and peeks at you. Too much, too soon.",
        (ParkTemperament.Mischievous, ParkCommand.Play) => $"{name} grabs the ball and runs off with it. That is not how the game works.",
        (ParkTemperament.Mischievous, _) => $"{name} pretends not to hear you, then sticks out its tongue.",
        (ParkTemperament.Feisty, _) => Pick(roll, $"{name} huffs and turns its back on you.", $"{name} is in no mood. It stomps off."),
        _ => $"{name} turns its back on you. Maybe later."
    };
}
