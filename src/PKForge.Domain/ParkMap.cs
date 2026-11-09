namespace PKForge.Domain;

[Flags]
public enum ParkTraversal { Land = 1, Water = 2, Lava = 4, Air = 8 }
public enum ParkTerrain { Blocked, Land, Water, Lava, Air }
public readonly record struct ParkPoint(float X, float Y);

/// <summary>What a Pokémon can do at a spot of the map. Daycare is Blissey's door.</summary>
public enum ParkSpotKind { Statue, Flag, Door, Shore, Berries, Gate, Nap, Hide, Daycare, Pass }

/// <summary>A place on the map where a Pokémon stands to do something, facing a direction
/// (PMD order: 0 south, 2 east, 4 north, 6 west).</summary>
public sealed record ParkSpot(ParkSpotKind Kind, float X, float Y, int Facing);

/// <summary>A park map, in the art's native pixels. The ground is a grid of 8 px cells:
/// '.' land, '~' water, '^' lava, '=' open air, '#' blocked. Its art lives in
/// pokepark/{Id}/: ground.png, objects.png and depth.png, all Width × Height.</summary>
public sealed partial class ParkMap
{
    public const int Cell = 8;
    public string Id { get; }
    /// <summary>The area's name, as the status line shows it.</summary>
    public string Name { get; init; } = "";
    /// <summary>The habitat the area's talk and moods come from.</summary>
    public ParkEnvironmentId Habitat { get; init; }
    public IReadOnlyList<ParkSpot> Spots { get; init; } = [];
    public int Width { get; }
    public int Height { get; }
    private readonly ParkTerrain[] _cells;
    private readonly int _columns;

    public ParkMap(string id, params string[] rows)
    {
        if (rows.Length == 0 || rows.Any(row => row.Length != rows[0].Length))
            throw new ArgumentException("Every row of the collision grid must have the same length.", nameof(rows));
        Id = id;
        _columns = rows[0].Length;
        Width = _columns * Cell; Height = rows.Length * Cell;
        _cells = rows.SelectMany(row => row.Select(cell => cell switch
        {
            '.' => ParkTerrain.Land, '~' => ParkTerrain.Water, '^' => ParkTerrain.Lava, '=' => ParkTerrain.Air,
            '#' => ParkTerrain.Blocked,
            _ => throw new ArgumentException($"Unknown collision cell '{cell}'.", nameof(rows))
        })).ToArray();
    }

    public ParkTerrain TerrainAt(float x, float y) => x < 0 || y < 0 || x >= Width || y >= Height
        ? ParkTerrain.Blocked : _cells[(int)y / Cell * _columns + (int)x / Cell];
    public static ParkTraversal TraversalFor(ParkTerrain terrain) => terrain switch
    {
        ParkTerrain.Land => ParkTraversal.Land, ParkTerrain.Water => ParkTraversal.Water,
        ParkTerrain.Lava => ParkTraversal.Lava, ParkTerrain.Air => ParkTraversal.Air, _ => 0
    };

    /// <summary>Tests the feet only: objects are drawn in depth order, so a resident may stand
    /// behind a tree crown or the statue, never on a trunk, a wall or a bush.</summary>
    public bool CanOccupy(float x, float y, ParkTraversal allowed, float radius = 6)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(radius) || radius < 0) return false;
        var left = (int)MathF.Floor(x - radius); var right = (int)MathF.Ceiling(x + radius) - 1;
        var top = (int)MathF.Floor(y - radius * .6f); var bottom = (int)MathF.Ceiling(y + radius * .3f) - 1;
        if (left < 0 || top < 0 || right >= Width || bottom >= Height) return false;
        for (var cy = top / Cell; cy <= bottom / Cell; cy++)
        for (var cx = left / Cell; cx <= right / Cell; cx++)
            if ((TraversalFor(_cells[cy * _columns + cx]) & allowed) == 0) return false;
        return true;
    }

    /// <summary>The park's places, each with its name and habitat. The first that holds a point
    /// names it; the last covers the whole map.</summary>
    public IReadOnlyList<ParkRegion> Regions { get; init; } = [];

    /// <summary>The place a point is in.</summary>
    public ParkRegion RegionAt(float x, float y) =>
        Regions.FirstOrDefault(r => r.Contains(x, y)) ?? new ParkRegion(Id, Name, Habitat, 0, 0, Width, Height);

    /// <summary>The top-left of spritedmistery's home park inside the map: the campfire, the house's
    /// window and the race track are placed from it.</summary>
    public ParkPoint HomeOrigin { get; init; }

    /// <summary>A point of the home park, given in the home park's own pixels.</summary>
    public ParkPoint AtHome(float x, float y) => new(HomeOrigin.X + x, HomeOrigin.Y + y);

    /// <summary>The Poképark: the artist's whole map, one world.</summary>
    public static ParkMap Park { get; } = World();
}

/// <summary>A named place of the park: a rectangle of the map in art pixels. High places (the
/// summit, the crater's rim, the shrine's plateau, the lighthouse point) are where flyers perch.</summary>
public sealed record ParkRegion(string Id, string Name, ParkEnvironmentId Habitat, int Left, int Top, int Width, int Height, bool High = false)
{
    public bool Contains(float x, float y) => x >= Left && y >= Top && x < Left + Width && y < Top + Height;
    public ParkPoint Center => new(Left + Width / 2f, Top + Height / 2f);
}
