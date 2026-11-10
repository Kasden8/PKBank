namespace PKForge.Domain;

/// <summary>
/// How the martial artists of the park speak, family by family, when they train you: Machamp
/// counts reps, Hitmonlee lives for kicks, Lucario reads your aura, Primeape takes everything
/// personally, Hariyama wants a proper sumo stance, Conkeldurr talks about beams… Lines rotate
/// per resident so the same one does not come back twice in a row.
/// </summary>
public static class ParkVoices
{
    /// <summary>What a master says before, during and after a sparring session. {0} is its name,
    /// {1} the player's best so far.</summary>
    public sealed record Dojo(string[] Intro, string[] Feint, string[] Perfect, string[] Good, string[] Poor, string[] Talk);

    private static readonly Dojo Machop = new(
        ["{0} cracks its knuckles. \"Eight strikes. Block them all, and we can talk about real training.\"",
         "{0} stretches every muscle it has, which takes a while. Then it points at you: your turn.",
         "{0} hands you an imaginary weight. You get the message: warm-up is over."],
        ["{0} feints and laughs. Strength is nothing without patience."],
        ["{0} lifts you off the ground in a bear hug. A perfect session! It looks genuinely proud."],
        ["{0} nods slowly. Good form. Same time tomorrow, and the day after, and the day after that."],
        ["{0} sighs. It shows you how to stand again, very slowly, like you are a Machop on its first day."],
        ["{0} is doing push-ups. It does not stop to talk, but it counts them out loud for you.",
         "{0} offers to carry you around the park. For training. Yours or its, it is not clear.",
         "{0} flexes, then looks at your arms, then flexes again, kindly."]);

    private static readonly Dojo Kicker = new(
        ["{0} bounces on its toes. \"Watch the feet, not the eyes.\"",
         "{0} draws a circle in the dirt with one foot. You stay inside, it attacks from outside."],
        ["{0} stops its kick an inch from your nose. Too early!"],
        ["{0} bows deeply. Nobody has blocked all its kicks in a long time."],
        ["{0} gives you a thumbs up with a foot. That is a compliment, coming from it."],
        ["{0} shows you the kick again in slow motion. Then faster. Then you get hit anyway."],
        ["{0} is kicking a leaf out of the air, over and over. It never misses.",
         "{0} tells you, without words, that hands are overrated."]);

    private static readonly Dojo Aura = new(
        ["{0} closes its eyes. It can feel where you will move before you do. \"Again.\"",
         "{0} reads your aura. It looks a little worried about your stance."],
        ["{0} does not even move. Your nerves gave you away."],
        ["{0} opens its eyes, surprised. Your aura was perfectly calm. It bows to you as an equal."],
        ["{0} smiles. Your aura is steadier than last time."],
        ["{0} places a paw on your chest. Breathe first, then block."],
        ["{0} meditates by the lake. When you sit down, it shifts over to make room.",
         "{0} senses something about you today. It does not say what. It just keeps you company."]);

    private static readonly Dojo Rage = new(
        ["{0} is already furious. You have not done anything yet.",
         "{0} stomps the ground. It takes this sparring session very, very personally."],
        ["{0} fakes a punch and screams. You flinch. It is delighted."],
        ["{0} is speechless with rage. You blocked everything. It demands a rematch immediately."],
        ["{0} grunts. That might be respect. Hard to tell with it."],
        ["{0} cannot believe how bad that was. It is angry on your behalf."],
        ["{0} is angry at the clouds today. Nothing personal.",
         "{0} glares at you, then at a tree, then at you again. It lets you stay."]);

    private static readonly Dojo Sumo = new(
        ["{0} stamps one foot, then the other, then claps. A proper sumo start.",
         "{0} lowers its hips and waits. It will not move until you get your stance right."],
        ["{0} barely twitched and you jumped. Steady, steady."],
        ["{0} slaps its belly in delight. You held your ground every time!"],
        ["{0} nods. You did not get pushed out of the ring once. Almost."],
        ["{0} gently pushes you over with one hand. It helps you back up with the other."],
        ["{0} is eating a very large lunch. Training starts after.",
         "{0} tries to teach you the stamping ritual. The ground shakes. You do not."]);

    private static readonly Dojo Builder = new(
        ["{0} drops its beam with a thud. \"Block like you're holding a wall up.\"",
         "{0} points at its pillar, then at you. You are the pillar today."],
        ["{0} swings, stops, grins. A good builder measures twice."],
        ["{0} slams its pillar into the ground in celebration. Rock solid!"],
        ["{0} gives you a nod. You'd make a decent support beam."],
        ["{0} looks at you like a wall that needs another layer of cement."],
        ["{0} is carrying something heavy across the park for no clear reason. It looks very happy.",
         "{0} asks you to hold its beam for a second. It is much heavier than it looks."]);

    private static readonly Dojo Judo = new(
        ["{0} tightens its belt. \"Respect first. Then we fight.\" It bows. You bow back.",
         "{0} bows, very correctly. It expects the same from you."],
        ["{0} grabs your sleeve and lets go. A lesson about attention."],
        ["{0} bows deeper than ever. A clean session, start to finish."],
        ["{0} bows. Correct. Room to grow."],
        ["{0} reties its belt, then reties yours. You will be back."],
        ["{0} is tying and retying its belt until the knot is perfect.",
         "{0} bows to you from across the park. You feel obliged to bow back."]);

    private static readonly Dojo Graceful = new(
        ["{0} flows from one stance to another like water. \"Follow me.\"",
         "{0} spins once and stops, perfectly still. You did not see its paws move."],
        ["{0}'s strike was a dance step. You fell for it."],
        ["{0} applauds with its long sleeves. Graceful work!"],
        ["{0} tilts its head approvingly. You are starting to flow."],
        ["{0} sighs at your footwork, then shows it again, beautifully."],
        ["{0} practices a combo so fast its sleeves crack like whips.",
         "{0} teaches a falling leaf a lesson about balance."]);

    private static readonly Dojo Delinquent = new(
        ["{0} chews its leaf and squints at you. \"You sure about this?\"",
         "{0} rolls its shoulders and smirks. It has been waiting for someone to try."],
        ["{0} fakes you out and snickers."],
        ["{0} drops its leaf in shock. Then it picks it back up, pretending it was on purpose. Respect."],
        ["{0} shrugs. Not bad, for you."],
        ["{0} pats you on the head. Condescendingly."],
        ["{0} leans on the statue like it owns the park.",
         "{0} lets you know, with one look, that this is its spot."]);

    private static readonly Dojo Showboat = new(
        ["{0} climbs onto the statue's plinth and strikes a pose for an invisible crowd. Then it jumps down to face you.",
         "{0} points at you, then at the sky, then at you again. The match is on!"],
        ["{0} feints and turns to the crowd. Nobody is there. It bows anyway."],
        ["{0} raises your arm like a champion. The crowd (also invisible) goes wild."],
        ["{0} gives you a high five mid-air."],
        ["{0} pins you, counts to three, and celebrates for a long, long time."],
        ["{0} practices its entrance. Again. And again.",
         "{0} flies off the cabin roof in a flying press. The landing could use work."]);

    private static readonly Dojo Master = new(
        ["{0} unrolls an old scroll and studies you. \"The tower teaches patience. Begin.\"",
         "{0} stands in a stance older than the park. You are the student today."],
        ["{0} did not strike. It wanted to see what you would do."],
        ["{0} rolls up its scroll and bows. You are ready for the next lesson."],
        ["{0} nods once. Discipline is showing."],
        ["{0} sits you down for a long talk about focus."],
        ["{0} is reading its scroll under a tree. It reads the same line for an hour.",
         "{0} tells you, without a word, that training is a path and not a place."]);

    private static readonly Dojo Generic = new(
        ["{0} takes a fighting stance and nods at you. \"Show me what you've got.\"",
         "{0} wants to train with you. It wants it a lot."],
        ["{0} feints. Gotcha."],
        ["{0} is impressed. Every single strike, blocked!"],
        ["{0} nods. You are getting better."],
        ["{0} shakes its head and shows you the basics again."],
        ["{0} is practicing its moves, very seriously.",
         "{0} challenges a tree to a duel. The tree loses."]);

    public static Dojo For(int species) => species switch
    {
        66 or 67 or 68 => Machop,
        106 or 107 or 236 or 237 => Kicker,
        447 or 448 => Aura,
        56 or 57 or 979 => Rage,
        296 or 297 => Sumo,
        532 or 533 or 534 => Builder,
        538 or 539 => Judo,
        619 or 620 or 307 or 308 => Graceful,
        674 or 675 or 559 or 560 => Delinquent,
        701 => Showboat,
        891 or 892 => Master,
        _ => Generic
    };

    /// <summary>The line at that turn of the rotation, with the name filled in.</summary>
    public static string Line(string[] lines, int turn, string name, int best = 0) =>
        string.Format(lines[((turn % lines.Length) + lines.Length) % lines.Length], name, best);
}
