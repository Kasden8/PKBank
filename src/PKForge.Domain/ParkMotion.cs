namespace PKForge.Domain;

public readonly record struct ParkResidentState(float X, float Y, bool IsWalking, int Facing, string Mood, string Activity);

/// <summary>Direction helpers shared by the park's walkers.</summary>
public static class ParkMotion
{
    /// <summary>PMDCollab's actual row order: S, SE, E, NE, N, NW, W, SW.</summary>
    public static int Facing(float dx, float dy)
    {
        if (dx == 0 && dy == 0) return 0;
        return ((int)Math.Round(Math.Atan2(dx, dy) / (Math.PI / 4)) + 8) % 8;
    }
}
