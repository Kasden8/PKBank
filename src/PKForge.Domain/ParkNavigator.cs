using System.Runtime.CompilerServices;

namespace PKForge.Domain;

/// <summary>Routes on a map's 8 px cells for one way of moving (land, or land and water).
/// A cell is open when a Pokémon's feet fit at its center; two cells connect when the feet
/// fit all along the way between them. Built once per map and traversal.</summary>
public sealed class ParkNavigator
{
    public const float Radius = 6;
    private const int StringReach = 24;
    private static readonly ConditionalWeakTable<ParkMap, Dictionary<ParkTraversal, ParkNavigator>> Cache = new();

    private readonly ParkMap _map;
    private readonly int _columns, _rows;
    private readonly bool[] _open;
    private readonly int[][] _edges;
    private readonly int[] _component;

    public ParkTraversal Allowed { get; }
    /// <summary>Every open cell, in reading order.</summary>
    public IReadOnlyList<int> OpenCells { get; }

    public static ParkNavigator For(ParkMap map, ParkTraversal allowed)
    {
        var byTraversal = Cache.GetValue(map, _ => new());
        lock (byTraversal)
        {
            if (!byTraversal.TryGetValue(allowed, out var navigator))
                byTraversal[allowed] = navigator = new ParkNavigator(map, allowed);
            return navigator;
        }
    }

    private ParkNavigator(ParkMap map, ParkTraversal allowed)
    {
        _map = map; Allowed = allowed;
        _columns = map.Width / ParkMap.Cell; _rows = map.Height / ParkMap.Cell;
        _open = new bool[_columns * _rows];
        for (var i = 0; i < _open.Length; i++)
        {
            var center = Center(i);
            _open[i] = map.CanOccupy(center.X, center.Y, allowed, Radius);
        }
        _edges = new int[_open.Length][];
        for (var i = 0; i < _open.Length; i++)
        {
            var edges = new List<int>(8);
            if (_open[i])
            {
                int x = i % _columns, y = i / _columns;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 || x + dx < 0 || x + dx >= _columns || y + dy < 0 || y + dy >= _rows) continue;
                    var next = (y + dy) * _columns + x + dx;
                    if (!_open[next]) continue;
                    // No corner cutting: a diagonal needs both of its side cells open.
                    if (dx != 0 && dy != 0 && (!_open[y * _columns + x + dx] || !_open[(y + dy) * _columns + x])) continue;
                    if (Clear(Center(i), Center(next))) edges.Add(next);
                }
            }
            _edges[i] = edges.ToArray();
        }
        _component = new int[_open.Length];
        Array.Fill(_component, -1);
        var label = 0;
        var queue = new Queue<int>();
        for (var start = 0; start < _open.Length; start++)
        {
            if (!_open[start] || _component[start] >= 0) continue;
            _component[start] = label;
            queue.Enqueue(start);
            while (queue.TryDequeue(out var cell))
                foreach (var next in _edges[cell])
                    if (_component[next] < 0) { _component[next] = label; queue.Enqueue(next); }
            label++;
        }
        OpenCells = Enumerable.Range(0, _open.Length).Where(i => _open[i]).ToArray();
    }

    public ParkPoint Center(int cell) => new(cell % _columns * ParkMap.Cell + ParkMap.Cell / 2f, cell / _columns * ParkMap.Cell + ParkMap.Cell / 2f);

    public bool CanStand(float x, float y) => _map.CanOccupy(x, y, Allowed, Radius);

    /// <summary>The open cell nearest to a point, or -1 on a map with no open cell.</summary>
    // The answer for each cell, worked out once (the map never changes): -2 not yet known. The ring
    // search below can take thousands of steps from the middle of a lake.
    private int[]? _nearCache;

    public int CellNear(float x, float y)
    {
        int cx = Math.Clamp((int)(x / ParkMap.Cell), 0, _columns - 1), cy = Math.Clamp((int)(y / ParkMap.Cell), 0, _rows - 1);
        var cache = _nearCache ??= Enumerable.Repeat(-2, _columns * _rows).ToArray();
        var key = cy * _columns + cx;
        // An open cell is its own answer. A closed one gets the open cell nearest its centre, worked
        // out once: from the asking point it would depend on who asked first, and the navigator is
        // shared by every park on the map.
        if (_open[key]) return key;
        var known = Volatile.Read(ref cache[key]);
        if (known != -2) return known;
        var found = CellNearSearch((cx + .5f) * ParkMap.Cell, (cy + .5f) * ParkMap.Cell, cx, cy);
        Volatile.Write(ref cache[key], found);
        return found;
    }

    private int CellNearSearch(float x, float y, int cx, int cy)
    {
        for (var ring = 0; ring < Math.Max(_columns, _rows); ring++)
        {
            var best = -1; var bestDistance = float.MaxValue;
            for (var dy = -ring; dy <= ring; dy++)
            for (var dx = -ring; dx <= ring; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= _columns || ny >= _rows || !_open[ny * _columns + nx]) continue;
                var c = Center(ny * _columns + nx);
                var d = (c.X - x) * (c.X - x) + (c.Y - y) * (c.Y - y);
                if (d < bestDistance) { bestDistance = d; best = ny * _columns + nx; }
            }
            if (best >= 0) return best;
        }
        return -1;
    }

    public bool Connected(ParkPoint a, ParkPoint b)
    {
        int from = CellNear(a.X, a.Y), to = CellNear(b.X, b.Y);
        return from >= 0 && to >= 0 && _component[from] == _component[to];
    }

    /// <summary>The way from one point to another as a few straight legs, ending on the target
    /// (or on the nearest open cell when the target itself cannot be stood on). Null when unreachable.</summary>
    public IReadOnlyList<ParkPoint>? Route(ParkPoint from, ParkPoint to)
    {
        int start = CellNear(from.X, from.Y), goal = CellNear(to.X, to.Y);
        if (start < 0 || goal < 0 || _component[start] != _component[goal]) return null;
        // Through the center of a start cell: a Pokémon stopped off-center must not cut a corner,
        // so when the straight way to the nearest cell clips a wall it goes round by an L, or
        // starts from another cell close by that it can walk to straight.
        var points = new List<ParkPoint>(8) { from };
        var home = Center(start);
        if (!Clear(from, home))
        {
            var corner = new[] { new ParkPoint(from.X, home.Y), new ParkPoint(home.X, from.Y) }.FirstOrDefault(c => Clear(from, c) && Clear(c, home));
            if (corner != default) points.Add(corner);
            else if (NearbyStart(from, _component[start]) is var other and >= 0) { start = other; home = Center(start); }
        }
        var cells = Search(start, goal);
        points.Add(home);
        points.AddRange(cells.Select(Center));
        points.Add(CanStand(to.X, to.Y) && Clear(points[^1], to) ? to : Center(goal));
        // Pull the string: keep only the corners, so Pokémon walk straight, not cell by cell.
        var legs = new List<ParkPoint>();
        var anchor = 0;
        // The way into the start cell is kept as it is: only cell-to-cell legs get straightened.
        if (points.Count > 2 && !Clear(from, points[2])) { legs.Add(points[1]); anchor = 1; }
        while (anchor < points.Count - 1)
        {
            // A straight leg reaches at most this many cells ahead: long walks are made of legs
            // anyway, and trying the far end of a route across the whole park every time is slow.
            var reach = anchor + 1;
            for (var next = Math.Min(points.Count - 1, anchor + StringReach); next > anchor + 1; next--)
                if (Clear(points[anchor], points[next])) { reach = next; break; }
            legs.Add(points[reach]);
            anchor = reach;
        }
        return legs;
    }

    // The open cell within two of a point, in the same component, that it can walk to straight: the nearest.
    private int NearbyStart(ParkPoint from, int component)
    {
        int cx = (int)(from.X / ParkMap.Cell), cy = (int)(from.Y / ParkMap.Cell);
        var best = -1; var bestDistance = float.MaxValue;
        for (var dy = -2; dy <= 2; dy++)
        for (var dx = -2; dx <= 2; dx++)
        {
            int nx = cx + dx, ny = cy + dy;
            if (nx < 0 || ny < 0 || nx >= _columns || ny >= _rows) continue;
            var cell = ny * _columns + nx;
            if (!_open[cell] || _component[cell] != component) continue;
            var c = Center(cell);
            var d = (c.X - from.X) * (c.X - from.X) + (c.Y - from.Y) * (c.Y - from.Y);
            if (d < bestDistance && Clear(from, c)) { bestDistance = d; best = cell; }
        }
        return best;
    }

    // The search's working arrays, kept between searches: a cell's cost and parent count only when
    // its stamp is this search's, so nothing is cleared or allocated per route (fresh arrays of
    // every cell each time went to the large-object heap and set off full garbage collections).
    private float[]? _cost;
    private int[]? _parent, _stamp;
    private int _search;
    private readonly PriorityQueue<int, float> _queue = new();
    private readonly object _searchGate = new();

    private List<int> Search(int start, int goal)
    {
        lock (_searchGate)
        {
            _cost ??= new float[_open.Length];
            _parent ??= new int[_open.Length];
            _stamp ??= new int[_open.Length];
            var stamp = ++_search;
            if (stamp == int.MaxValue) { Array.Clear(_stamp); _search = stamp = 1; }
            float Cost(int cell) => _stamp[cell] == stamp ? _cost[cell] : float.MaxValue;
            var queue = _queue;
            queue.Clear();
            _cost[start] = 0; _parent[start] = start; _stamp[start] = stamp;
            queue.Enqueue(start, 0);
            while (queue.TryDequeue(out var cell, out _))
            {
                if (cell == goal) break;
                var here = _cost[cell];
                foreach (var next in _edges[cell])
                {
                    var step = next % _columns != cell % _columns && next / _columns != cell / _columns ? 1.4142f : 1f;
                    if (here + step >= Cost(next)) continue;
                    _cost[next] = here + step; _parent[next] = cell; _stamp[next] = stamp;
                    int gx = goal % _columns - next % _columns, gy = goal / _columns - next / _columns;
                    queue.Enqueue(next, _cost[next] + MathF.Sqrt(gx * gx + gy * gy));
                }
            }
            var path = new List<int>();
            for (var cell = goal; cell != start; cell = _parent[cell]) path.Add(cell);
            path.Reverse();
            return path;
        }
    }

    /// <summary>Whether the feet fit all along a straight line. Exact: the cells under the feet only
    /// change where an edge of the feet crosses a cell line, so the feet are tested once in every
    /// stretch between two such crossings, however long the line.</summary>
    public bool Clear(ParkPoint a, ParkPoint b)
    {
        if (!CanStand(a.X, a.Y) || !CanStand(b.X, b.Y)) return false;
        float dx = b.X - a.X, dy = b.Y - a.Y;
        // One list per thread, reused: this runs dozens of times for every route.
        var crossings = _crossings ??= new List<float>(64);
        crossings.Clear();
        crossings.Add(0); crossings.Add(1);
        Crossings(a.X - Radius, dx, crossings);
        Crossings(a.X + Radius, dx, crossings);
        Crossings(a.Y - Radius * .6f, dy, crossings);
        Crossings(a.Y + Radius * .3f, dy, crossings);
        crossings.Sort();
        for (var i = 0; i + 1 < crossings.Count; i++)
        {
            var t = (crossings[i] + crossings[i + 1]) / 2;
            if (!CanStand(a.X + dx * t, a.Y + dy * t)) return false;
        }
        return true;
    }

    [ThreadStatic] private static List<float>? _crossings;

    // Where along the line (0 to 1) an edge moving from start by delta crosses a pixel line that
    // is a cell line, or reaches the pixel where the cell changes (CanOccupy rounds to pixels).
    private static void Crossings(float start, float delta, List<float> into)
    {
        if (delta == 0) return;
        var step = MathF.Sign(delta);
        for (var k = (int)MathF.Floor(start / ParkMap.Cell) + (step > 0 ? 1 : 0); ; k += (int)step)
        {
            // Both sides of the line: the edge's pixel rounding can put the change a pixel either way.
            for (var side = -1; side <= 1; side++)
            {
                var t = (k * ParkMap.Cell + side - start) / delta;
                if (t > 0 && t < 1) into.Add(t);
            }
            if ((k * ParkMap.Cell - start) / delta >= 1.01f || (k * ParkMap.Cell - start) / delta < -1) break;
        }
    }
}
