namespace PKForge.Domain;

/// <summary>What a Pokémon's body is doing. Each pose maps to PMD animations, best first.</summary>
public enum ParkPose
{
    Idle, Walk, Sleep, Wake, Eat, Sit, Lie, Nod, Hop, Pose, LookUp, Charge, Attack, Hurt,
    Tumble, Trip, DeepBreath, Float, Dance, Spin, Cringe, Sing, Pain, Pull, Fly
}

public static class ParkPoses
{
    /// <summary>The PMD animations to try for a pose, best first. Almost every species has Idle,
    /// Walk, Sleep, Hurt, Attack, Hop, Charge and Rotate; the richer set (Eat, Sit, Laying, Nod,
    /// Pose, LookUp, Trip…) exists for a few hundred, so every pose ends on a common one.</summary>
    public static IReadOnlyList<string> Animations(ParkPose pose) => pose switch
    {
        ParkPose.Walk => ["Walk"],
        ParkPose.Sleep => ["Sleep", "EventSleep", "Laying", "Idle"],
        ParkPose.Wake => ["Wake", "Idle"],
        ParkPose.Eat => ["Eat", "Idle"],
        ParkPose.Sit => ["Sit", "Idle"],
        ParkPose.Lie => ["Laying", "Sleep", "Idle"],
        ParkPose.Nod => ["Nod", "Hop", "Idle"],
        ParkPose.Hop => ["Hop", "Idle"],
        ParkPose.Pose => ["Pose", "Appeal", "Charge", "Hop", "Idle"],
        ParkPose.LookUp => ["LookUp", "Idle"],
        ParkPose.Charge => ["Charge", "Idle"],
        ParkPose.Attack => ["Attack", "Strike", "Swing", "Double", "Idle"],
        ParkPose.Hurt => ["Hurt", "Idle"],
        ParkPose.Tumble => ["Tumble", "TumbleBack", "Hurt", "Idle"],
        ParkPose.Trip => ["Trip", "LostBalance", "Hurt", "Idle"],
        ParkPose.DeepBreath => ["DeepBreath", "Idle"],
        ParkPose.Float => ["Float", "Idle"],
        ParkPose.Dance => ["Dance", "Rotate", "Hop", "Idle"],
        ParkPose.Spin => ["Rotate", "Spin", "Hop", "Idle"],
        ParkPose.Cringe => ["Cringe", "Hurt", "Idle"],
        ParkPose.Sing => ["Sing", "Sound", "Charge", "Idle"],
        ParkPose.Pain => ["Pain", "Hurt", "Idle"],
        ParkPose.Pull => ["Pull", "Eat", "Idle"],
        // A flyer's own flight when its sprite has one; otherwise its resting pose (Articuno's wings
        // spread wide), never a walk, which looks like walking on air.
        ParkPose.Fly => ["FlapAround", "Hover", "Idle"],
        _ => ["Idle"]
    };

    /// <summary>Loop the animation while the pose lasts, or play it once and hold its last frame.</summary>
    public static bool Loops(ParkPose pose) => pose is ParkPose.Idle or ParkPose.Walk or ParkPose.Sleep
        or ParkPose.Eat or ParkPose.Sit or ParkPose.Lie or ParkPose.Nod or ParkPose.Hop or ParkPose.Charge
        or ParkPose.Float or ParkPose.Dance or ParkPose.Spin or ParkPose.Sing or ParkPose.Pain or ParkPose.Fly;
}
