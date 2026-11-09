namespace PKForge.Domain;

/// <summary>One object of a map's objects layer. Pixels are indices into the full layer.
/// Base is the row its foot stands on: residents whose feet are above it are drawn behind it.</summary>
public sealed record ParkPiece(int Left, int Top, int Width, int Height, int[] Pixels, int Base);

public static class ParkLayerCut
{
    /// <summary>Splits the fully opaque pixels into pieces drawn in depth order. With bases (one per
    /// pixel, from the map's depth.png) a piece is the touching pixels that share a base, so one
    /// drawing of many trees still sorts tree by tree; without, a piece is the touching pixels
    /// and its base the row below its lowest one. Soft pixels (the artist's shadows) belong to no
    /// piece: they are painted flat on the ground.</summary>
    public static IReadOnlyList<ParkPiece> Split(ReadOnlySpan<byte> alpha, int width, int height, ReadOnlySpan<int> bases = default)
    {
        if (width <= 0 || height <= 0 || alpha.Length != width * height)
            throw new ArgumentException("The alpha mask does not match the layer size.", nameof(alpha));
        if (!bases.IsEmpty && bases.Length != alpha.Length)
            throw new ArgumentException("The depth map does not match the layer size.", nameof(bases));
        var seen = new bool[alpha.Length];
        var pieces = new List<ParkPiece>();
        var stack = new Stack<int>();
        var pixels = new List<int>();
        for (var start = 0; start < alpha.Length; start++)
        {
            if (seen[start] || alpha[start] != 255) continue;
            var depth = bases.IsEmpty ? 0 : bases[start];
            seen[start] = true;
            stack.Push(start);
            pixels.Clear();
            int left = width, top = height, right = -1, bottom = -1;
            while (stack.TryPop(out var i))
            {
                pixels.Add(i);
                int x = i % width, y = i / width;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    var n = ny * width + nx;
                    if (seen[n] || alpha[n] != 255 || !bases.IsEmpty && bases[n] != depth) continue;
                    seen[n] = true;
                    stack.Push(n);
                }
            }
            pieces.Add(new(left, top, right - left + 1, bottom - top + 1, pixels.ToArray(), bases.IsEmpty ? bottom + 1 : depth));
        }
        return pieces;
    }
}
