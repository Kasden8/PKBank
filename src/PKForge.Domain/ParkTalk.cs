namespace PKForge.Domain;

/// <summary>
/// What a resident says (or does) when you talk to it, from what it is: its types, the time of
/// day, the weather, how hungry or tired it is, and how close you two are. Every line has an id
/// so a resident does not say the same thing again soon.
/// </summary>
public static class ParkTalk
{
    public sealed record Line(string Id, string Text);

    private static readonly Dictionary<int, string[]> ByType = new()
    {
        [ParkType.Normal] =
        [
            "{0} sniffs your shoes very thoroughly, then decides you can stay.",
            "{0} sits on your foot so you cannot leave without it noticing.",
            "{0} brings you a stick. It is not clear what you are supposed to do with it, but it waits.",
            "{0} yawns, stretches, and leans against your leg like you are furniture.",
            "{0} tells you a long story in its own language. You nod at the right moments.",
        ],
        [ParkType.Fire] =
        [
            "{0} holds its warm side toward your hands. You did not know they were cold until now.",
            "{0} lets out a tiny puff of smoke and looks very proud of it.",
            "{0} steers well clear of the lake today, just in case.",
            "{0} glows a little brighter when you say its name.",
            "{0} warms a pebble for you to hold in your pocket.",
        ],
        [ParkType.Water] =
        [
            "{0} squirts a little water at your boots, then looks innocent.",
            "{0} is still dripping from the lake and wants a hug anyway.",
            "{0} blows a bubble that floats up and pops on your nose.",
            "{0} shows you a smooth stone it found at the bottom of the lake.",
            "{0} shakes off right next to you. On purpose, this time.",
        ],
        [ParkType.Grass] =
        [
            "{0} turns toward the sun and closes its eyes. Talking can wait.",
            "{0} smells like freshly cut grass and is very pleased about it.",
            "{0} offers you a leaf. It seems important to it that you keep it.",
            "{0} rustles softly when you sit next to it. That is its way of humming.",
            "{0} has a ladybug sleeping on its head and will not move until it wakes.",
        ],
        [ParkType.Electric] =
        [
            "Your hair stands on end as {0} comes closer. It finds that hilarious.",
            "{0}'s fur crackles. Your phone's battery jumps a few percent.",
            "{0} sparks a little when it is excited, and it is very excited to see you.",
            "{0} touches its nose to yours. Static. You both jump.",
            "{0} hums like a power line on a quiet evening.",
        ],
        [ParkType.Ice] =
        [
            "{0} breathes out a small cloud of frost and draws a snowflake in it.",
            "{0} leans against you and you shiver. It does not notice.",
            "{0} slides across a frozen patch of grass that was not frozen a second ago.",
            "{0} offers you a perfectly round snowball. Throwing it would be rude.",
        ],
        [ParkType.Fighting] =
        [
            "{0} punches the air at a steady rhythm while you talk. It is listening, mostly.",
            "{0} looks at your posture and fixes it without asking.",
        ],
        [ParkType.Poison] =
        [
            "{0} keeps a polite distance, for your sake.",
            "{0} oozes a little when happy. It is very happy to see you.",
            "{0} offers you something to eat. You decide to say no thank you.",
            "{0} smells of something you cannot place. It is proud of that smell.",
        ],
        [ParkType.Ground] =
        [
            "{0} has been rolling in the dirt again, and wants you to know it.",
            "{0} digs a small hole, inspects it, and fills it back in.",
            "{0} stamps the ground. Somewhere, a Diglett pops its head up to complain.",
            "{0} leaves a perfect paw print in the mud next to your footprint.",
        ],
        [ParkType.Flying] =
        [
            "{0} circles over your head once and lands right on your shoulder.",
            "{0} preens a feather until it is perfect, then gives it to you.",
            "{0} spreads its wings to show you how big it is now.",
            "{0} points at a cloud, then at itself. It wants to go up there.",
        ],
        [ParkType.Psychic] =
        [
            "{0} answers the question you were about to ask. You forget what it was.",
            "{0} stares at a spot behind you for a long time. You do not turn around.",
            "{0} floats a berry over to you without touching it.",
            "{0} already knew you were coming. It saved you a spot.",
        ],
        [ParkType.Bug] =
        [
            "{0} has collected seven leaves today and wants you to see every one.",
            "{0} buzzes around your head twice before settling on your sleeve.",
            "{0} spins a thread between two blades of grass. It is a bridge. For ants.",
            "{0} chirps at a frequency only it and the other Bug types can hear.",
        ],
        [ParkType.Rock] =
        [
            "{0} sits perfectly still. Is it asleep? It is hard to tell with rocks.",
            "{0} lets you knock on it. It sounds hollow. It looks offended.",
            "{0} has found a nice warm stone and is lying on it. Stones together.",
            "{0} rolls a short way toward you. That is a big gesture, for it.",
        ],
        [ParkType.Ghost] =
        [
            "{0} was behind you this whole time. You still jump.",
            "{0} makes your shadow wave at you. Your actual hand did not move.",
            "{0} turns half invisible, then giggles at your face.",
            "{0} floats through the cabin wall and back again, just to show it can.",
            "{0} whispers something. You did not catch it. Maybe that is for the best.",
        ],
        [ParkType.Dragon] =
        [
            "{0} lowers its head so you can pat it, but only once. It has a reputation.",
            "{0} lets out a small roar at the sky, then looks at you to check you were impressed.",
            "{0} guards the patch of grass it slept on like a treasure.",
            "{0} has decided you are part of its hoard now.",
        ],
        [ParkType.Dark] =
        [
            "{0} watches you from the shadow of the statue and only comes out when you look away.",
            "{0} took something from your pocket. It gives it back when you notice. Mostly.",
            "{0} grins in a way that makes you check your pockets.",
            "{0} pretends not to care that you came. Its tail says otherwise.",
        ],
        [ParkType.Steel] =
        [
            "{0} has polished itself until you can see your reflection. It waits for compliments.",
            "{0} clinks when it walks and is very pleased about the sound.",
            "{0} is cold to the touch and warm in every other way.",
            "{0} stands guard next to you, very seriously, for no reason at all.",
        ],
        [ParkType.Fairy] =
        [
            "{0} leaves a trail of tiny sparkles where it walks.",
            "{0} puts a flower behind your ear and steps back to admire its work.",
            "{0} twirls once, just because you are watching.",
            "{0} smells faintly of sweets. You get hungry just standing there.",
        ],
    };

    // Families with a voice of their own, beyond their types.
    private static readonly (string Key, int[] Species, string[] Lines)[] ByFamily =
    [
        ("eevee", [133, 134, 135, 136, 196, 197, 470, 471, 700],
        [
            "{0} tilts its head and asks, somehow, what you think it should evolve into. Then it decides it likes itself as it is.",
            "{0} keeps an eye on the other Eevee evolutions in the park, like a big family reunion.",
            "{0} sniffs your bag for stones. It is curious about all of them and afraid of none.",
        ]),
        ("charmander", [4, 5, 6],
        [
            "{0} checks that its tail flame is burning bright, then shows you, just to be sure.",
            "{0} keeps its tail high above the wet grass. Always.",
            "{0} lets off a tiny ember when it laughs.",
        ]),
        ("squirtle", [7, 8, 9],
        [
            "{0} puts on an imaginary pair of sunglasses and nods very coolly.",
            "{0} pulls into its shell, then pops out to see if you were scared.",
            "{0} sprays a perfect arc of water over your head and into the lake.",
        ]),
        ("bulbasaur", [1, 2, 3],
        [
            "{0} lets you smell the bud on its back. It smells like rain on warm earth.",
            "{0} stretches its vines out to tap your shoulder hello.",
            "{0} is soaking up the sun so seriously that it does not want to move.",
        ]),
        ("dragon", [147, 148, 149, 371, 372, 373, 443, 444, 445, 610, 611, 612, 633, 634, 635, 704, 705, 706, 782, 783, 784, 885, 886, 887, 996, 997, 998],
        [
            "{0} lowers its great head to your level. For a dragon, that is a sign of real respect.",
            "{0} tells you, without words, that one day it will fly you over the clouds.",
            "{0} curls up around you like you are something worth guarding.",
        ]),
        ("ralts", [280, 281, 282, 475],
        [
            "{0} feels what you feel before you say a word. Today, it seems glad.",
            "{0} sways to the rhythm of your mood. You did not know you had one.",
            "{0} stands a little in front of you, as if ready to protect you.",
        ]),
        ("ghostly", [200, 429, 355, 356, 477, 607, 608, 609, 708, 709],
        [
            "{0} drifts close, and the air goes cold, and somehow it is comforting.",
            "{0} hums a tune nobody has sung in a hundred years.",
            "{0}'s little flame flickers when you laugh.",
        ]),
        ("abra", [63, 64, 65],
        [
            "{0} teleports a step to the left every time you get close. It is a game, probably.",
            "{0} bends a spoon, unbends it, and hands it back to you good as new.",
            "{0} is asleep and awake at the same time. Do not ask how.",
        ]),
        ("rock", [74, 75, 76, 95, 208],
        [
            "{0} lets out a low rumble. It is laughing, deep down in the ground.",
            "{0} has grown a little since you last checked. Rock grows slowly, but it grows.",
        ]),
        ("legend", [144, 145, 146, 150, 151, 243, 244, 245, 249, 250, 377, 378, 379, 380, 381, 382, 383, 384, 385, 386, 480, 481, 482, 483, 484, 487, 488, 491, 492, 493, 638, 639, 640, 643, 644, 646, 716, 717, 718, 791, 792, 800, 888, 889, 890],
        [
            "{0} regards you with eyes older than the park. For a moment the wind stops.",
            "{0} lets you stand next to it. That is more than most people ever get.",
            "{0} seems to be listening to something very far away.",
        ]),
        ("troll", [92, 93, 94, 302, 570, 571, 198, 430, 215, 461, 509, 510, 859, 860, 861, 674, 675, 261, 262],
        [
            "{0} grins at you. You get the feeling it has done something, and that you will find out soon.",
            "{0} is holding a berry it very clearly did not pick itself.",
            "{0} looks innocent. Too innocent.",
            "{0} points at another resident and laughs. You decide not to ask.",
        ]),
        ("lapras", [131],
        [
            "{0} sings a slow song from across the sea. Everyone near the lake stops to listen.",
            "{0} offers you a ride around the lake. It is a short trip, but it takes its time.",
        ]),
        ("gyarados", [130],
        [
            "{0} glares down at you. Then it remembers you knew it as a Magikarp, and looks away first.",
            "{0} roars at the sky. Nothing in particular made it angry. That is just how it says hello.",
        ]),
    ];

    private static readonly Dictionary<ParkDayPhase, string[]> ByPhase = new()
    {
        [ParkDayPhase.Morning] = ["{0} yawns hugely. Mornings are not its thing.", "{0} is full of energy this morning and wants to show you everything."],
        [ParkDayPhase.Evening] = ["{0} watches the sky turn orange with you.", "{0} is winding down for the day, slow and soft."],
        [ParkDayPhase.Night] = ["{0} blinks at you sleepily. What are you doing up so late?", "{0} looks at the stars, then at you, then back at the stars."],
    };

    private static readonly Dictionary<ParkWeather, string[]> ByWeather = new()
    {
        [ParkWeather.Rain] = ["{0} shakes the rain off its head and shuffles under your arm.", "{0} watches the raindrops land in the lake, one by one."],
        [ParkWeather.Storm] = ["{0} flinches at the thunder and stays very close to you.", "{0} counts the seconds between the flash and the boom."],
        [ParkWeather.Cloudy] = ["{0} looks at the grey sky and sighs. It wanted sunshine."],
        [ParkWeather.Sunny] = ["{0} is warm from lying in the sun and smells like summer."],
    };

    private static string[] Close =>
    [
        "{0} runs to you the moment it sees you.",
        "{0} rests its head against you and closes its eyes. It trusts you completely.",
        "{0} has been waiting for you by the path. It pretends it was not.",
    ];

    private static string[] Wary =>
    [
        "{0} keeps its distance and watches you carefully.",
        "{0} lets you talk, but it does not come closer. Not yet.",
    ];

    /// <summary>Every line that fits right now, with ids for not repeating. The species' own
    /// lines and needs running out come first.</summary>
    public static IReadOnlyList<Line> For(ParkLifeResident resident, ParkCareState care, ParkDayPhase phase, ParkWeather weather, int turn)
    {
        var name = resident.Name;
        var lines = new List<Line>();
        void From(string key, IEnumerable<string> texts)
        {
            var index = 0;
            foreach (var text in texts) lines.Add(new($"{key}{index++}", string.Format(text, name)));
        }
        if (ParkLore.TalkLine(resident.Species, name, turn) is { } own) lines.Add(new($"species{resident.Species}-{turn % 2}", own));
        if (resident.Types.Contains(ParkType.Fighting)) From("dojo", ParkVoices.For(resident.Species).Talk.Select(t => t.Replace("{0}", "{0}")));
        if (care.Hunger < ParkCare.Low) From("hungry", [$"{{0}}'s stomach growls. It looks at you, then at your bag, then at you again."]);
        if (care.Energy < ParkCare.Low) From("tired", ["{0} can barely keep its eyes open. It leans on you and starts to drift off."]);
        if (care.Social < ParkCare.Low) From("lonely", ["{0} perks up when you come over. It has been on its own a lot lately."]);
        if (care.Fun < ParkCare.Low) From("bored", ["{0} sighs dramatically. There is nothing to do. Nothing at all."]);
        foreach (var (key, species, family) in ByFamily)
            if (species.Contains(resident.Species)) From($"family{key}-", family);
        foreach (var type in resident.Types)
            if (ByType.TryGetValue(type, out var typed)) From($"type{type}-", typed);
        if (ByPhase.TryGetValue(phase, out var timed)) From($"phase{phase}-", timed);
        if (ByWeather.TryGetValue(weather, out var wet)) From($"weather{weather}-", wet);
        if (care.Bond >= 60) From("close", Close);
        else if (care.Bond < 15) From("wary", Wary);
        return lines;
    }

    /// <summary>Picks a line it has not said lately: needs first, then its species, then the rest by turn.</summary>
    public static Line Pick(IReadOnlyList<Line> lines, IReadOnlyCollection<string> recent, int turn)
    {
        var fresh = lines.Where(l => !recent.Contains(l.Id)).ToList();
        if (fresh.Count == 0) fresh = lines.ToList();
        var urgent = fresh.FirstOrDefault(l => l.Id.StartsWith("hungry") || l.Id.StartsWith("tired"));
        if (urgent is not null) return urgent;
        // Its own species' and family's lines come up more than the general ones.
        var own = fresh.FirstOrDefault(l => l.Id.StartsWith("species") || l.Id.StartsWith("family") || l.Id.StartsWith("dojo"));
        if (own is not null && turn % 2 == 0) return own;
        return fresh[((turn % fresh.Count) + fresh.Count) % fresh.Count];
    }
}
