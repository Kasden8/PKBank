namespace PKForge.Domain;

/// <summary>Where the park camera looks and how close. At rest the art is drawn at a whole-number
/// scale so every art pixel stays square; a pinch zooms smoothly through the scales in between,
/// and on letting go (or on a zoom button) the camera glides to the nearest whole one. By default the camera
/// shows about a home park's width of the map around the selected Pokémon and follows it;
/// dragging or pinching takes over until <see cref="Reset"/> or <see cref="Follow"/>.</summary>
public sealed class ParkViewport(int mapWidth, int mapHeight)
{
    private const int MaxZoomIn = 2;     // closest zoom: twice the default scale
    private const float ViewWidth = 320; // art pixels across the view at the default scale
    private const float Friction = .005f; // fling speed lost per ms, as a fraction
    private int? _zoom;          // the whole scale it settles on (null: the default)
    private float _scale;        // the scale shown now, easing toward it
    private bool _pinching;
    private (float X, float Y)? _focus; // the screen point that stays put while it eases
    private int _width = 1, _height = 1;
    private float _velocityX, _velocityY;
    private float _pinchStart;
    private (float X, float Y)? _flight;
    private float _stickX, _stickY, _stickZoom; // a gamepad's sticks, -1 to 1, held between events
    private const float StickSpeed = 1.1f;      // views crossed per second at full tilt
    private const float StickZoomSpeed = 1.6f;  // doublings of the scale per second at full tilt

    public float CenterX { get; private set; } = mapWidth / 2f;
    public float CenterY { get; private set; } = mapHeight / 2f;
    public bool Following { get; private set; } = true;
    /// <summary>The scale shown now: whole at rest, in between while it zooms.</summary>
    public float Scale => _scale > 0 ? Math.Clamp(_scale, MinScale, MaxScale) : Target;
    /// <summary>The whole scale the camera settles on.</summary>
    public int Target => Math.Clamp(_zoom ?? DefaultScale, MinScale, MaxScale);
    /// <summary>The scale the camera comes back to: about 320 art pixels across the view.</summary>
    public int DefaultScale => Math.Max(CoverScale, (int)MathF.Round(_width / ViewWidth));
    /// <summary>The smallest scale that fills the view.</summary>
    public int CoverScale => Math.Max(1, (int)MathF.Ceiling(MathF.Max(_width / (float)mapWidth, _height / (float)mapHeight)));
    /// <summary>The farthest the camera zooms out: the scale that fills the view, so nothing
    /// outside the map ever shows.</summary>
    public int MinScale => CoverScale;
    public int MaxScale => DefaultScale * MaxZoomIn;
    public bool IsMoving => _velocityX != 0 || _velocityY != 0 || _flight is not null || _stickX != 0 || _stickY != 0 || _stickZoom != 0 || Scale != Target && !_pinching;

    /// <summary>The top-left corner in art pixels, snapped to screen pixels.</summary>
    public (float Left, float Top) Corner
    {
        get
        {
            var scale = Scale;
            float viewWidth = _width / (float)scale, viewHeight = _height / (float)scale;
            return (MathF.Round(Edge(CenterX, viewWidth, mapWidth) * scale) / scale,
                MathF.Round(Edge(CenterY, viewHeight, mapHeight) * scale) / scale);
        }
    }

    // A view wider than the map centers it; otherwise the view stays inside the map.
    private static float Edge(float center, float view, float size) =>
        view >= size ? (size - view) / 2 : Math.Clamp(center - view / 2, 0, size - view);

    public void Resize(int width, int height)
    {
        _width = Math.Max(1, width); _height = Math.Max(1, height);
        Clamp();
    }

    /// <summary>Eases toward the target (art pixels) while following, over about a quarter second.</summary>
    public void Track(float x, float y, float elapsedMs)
    {
        if (!Following) return;
        var step = Math.Clamp(elapsedMs / 250f, 0, 1);
        CenterX += (x - CenterX) * step; CenterY += (y - CenterY) * step;
        Clamp();
    }

    /// <summary>Jumps straight to the target and follows it from now on.</summary>
    public void Follow(float x, float y)
    {
        Following = true; _velocityX = _velocityY = 0; _flight = null;
        CenterX = x; CenterY = y;
        Clamp();
    }

    /// <summary>Moves the map with a finger: dx and dy in screen pixels.</summary>
    public void Drag(float dx, float dy)
    {
        Following = false; _velocityX = _velocityY = 0; _flight = null;
        CenterX -= dx / Scale; CenterY -= dy / Scale;
        Clamp();
    }

    /// <summary>Lets the map glide after the finger leaves: speed in screen pixels per ms.</summary>
    public void Fling(float speedX, float speedY)
    {
        Following = false; _flight = null;
        _velocityX = -speedX / Scale; _velocityY = -speedY / Scale;
    }

    /// <summary>Glides to a point of the map (art pixels) over about half a second and stays there.</summary>
    public void FlyTo(float x, float y)
    {
        Following = false; _velocityX = _velocityY = 0;
        _flight = (x, y);
    }

    /// <summary>A gamepad's sticks, held until the next call: x and y (-1 to 1, past the dead zone)
    /// move the camera across the map, faster the further the stick leans; zoom (-1 to 1, up is
    /// negative) zooms in or out smoothly, and the camera settles on a whole scale when let go.</summary>
    public void Stick(float x, float y, float zoom)
    {
        if (x != 0 || y != 0) { Following = false; _velocityX = _velocityY = 0; _flight = null; }
        if (zoom != 0 && _stickZoom == 0) { Hold(); _focus = null; }
        if (zoom == 0 && _stickZoom != 0) _zoom = Math.Clamp((int)MathF.Round(Scale), MinScale, MaxScale);
        _stickX = x; _stickY = y; _stickZoom = zoom;
    }

    public void Step(float elapsedMs)
    {
        if (elapsedMs <= 0) return;
        // A frame late after a pause does not throw the camera across the map.
        var stickMs = Math.Min(elapsedMs, 100);
        if (_stickZoom != 0)
            ScaleAround(Math.Clamp(Scale * MathF.Pow(2, -_stickZoom * StickZoomSpeed * stickMs / 1000), MinScale, MaxScale), _width / 2f, _height / 2f);
        if (_stickX != 0 || _stickY != 0)
        {
            // In screen terms: the same feel at any zoom.
            CenterX += _stickX * StickSpeed * _width / Scale * stickMs / 1000;
            CenterY += _stickY * StickSpeed * _width / Scale * stickMs / 1000;
            Clamp();
        }
        // Easing to the scale it settles on, around the point it was zooming on.
        if (!_pinching && _stickZoom == 0 && Scale != Target)
        {
            var next = Scale + (Target - Scale) * Math.Clamp(elapsedMs / 70f, 0, 1);
            if (MathF.Abs(Target - next) < .02f) next = Target;
            var (zx, zy) = _focus ?? (_width / 2f, _height / 2f);
            ScaleAround(next, zx, zy);
            if (next == Target) _focus = null;
        }
        if (_flight is var (fx, fy))
        {
            var step = Math.Clamp(elapsedMs / 120f, 0, 1);
            var (x0, y0) = (CenterX, CenterY);
            CenterX += (fx - CenterX) * step; CenterY += (fy - CenterY) * step;
            Clamp();
            // Arrived, or held back by the map's edge.
            if (MathF.Abs(CenterX - x0) + MathF.Abs(CenterY - y0) < .05f) _flight = null;
            return;
        }
        if (!IsMoving) return;
        CenterX += _velocityX * elapsedMs; CenterY += _velocityY * elapsedMs;
        var keep = MathF.Pow(1 - Friction, elapsedMs);
        _velocityX *= keep; _velocityY *= keep;
        if (MathF.Abs(_velocityX) + MathF.Abs(_velocityY) < .001f) _velocityX = _velocityY = 0;
        var (x, y) = (CenterX, CenterY);
        Clamp();
        if (x != CenterX) _velocityX = 0;
        if (y != CenterY) _velocityY = 0;
    }

    /// <summary>A finger on the map stops a glide without leaving follow mode.</summary>
    public void Stop() { _velocityX = _velocityY = 0; _flight = null; }

    /// <summary>Follows the selected Pokémon again, keeping the zoom.</summary>
    public void Resume() { Following = true; _velocityX = _velocityY = 0; _flight = null; }

    public void BeginPinch() { _pinchStart = Scale; _pinching = true; _velocityX = _velocityY = 0; _flight = null; }

    /// <summary>factor: finger spread now ÷ at the start. The art pixel under the fingers'
    /// midpoint (screen pixels) stays under it.</summary>
    public void Pinch(float factor, float focusX, float focusY)
    {
        if (_pinchStart == 0) BeginPinch();
        var scale = Math.Clamp(_pinchStart * factor, MinScale, MaxScale);
        if (scale == Scale) return;
        Following = false;
        ScaleAround(scale, focusX, focusY);
        _focus = (focusX, focusY);
    }

    /// <summary>The fingers leave: the camera settles on the nearest whole scale.</summary>
    public void EndPinch()
    {
        _pinchStart = 0; _pinching = false;
        _zoom = Math.Clamp((int)MathF.Round(Scale), MinScale, MaxScale);
    }

    /// <summary>Zoom steps for the pad, around the view's center, gliding there.</summary>
    public void Zoom(int steps)
    {
        Hold();
        _stickZoom = 0;
        _zoom = Math.Clamp(Target + steps, MinScale, MaxScale);
        _focus = null;
    }

    // Keeps showing the scale it shows now while the one it settles on changes, so it glides there.
    private void Hold()
    {
        if (_scale <= 0) _scale = Target;
    }

    // Shows a scale keeping the art pixel under a screen point where it is.
    private void ScaleAround(float scale, float focusX, float focusY)
    {
        var (left, top) = Corner;
        float x = left + focusX / Scale, y = top + focusY / Scale;
        _scale = scale;
        CenterX = x - focusX / scale + _width / (2f * scale);
        CenterY = y - focusY / scale + _height / (2f * scale);
        Clamp();
    }

    /// <summary>As far out as the camera goes, centered on a point (for games played across a place).</summary>
    public void ShowAll(float x, float y)
    {
        Hold();
        _zoom = MinScale; Following = false; _velocityX = _velocityY = 0; _flight = null;
        CenterX = x; CenterY = y;
        Clamp();
    }

    /// <summary>Back to the default scale, following the selected Pokémon.</summary>
    public void Reset()
    {
        Hold();
        _zoom = null; Following = true; _velocityX = _velocityY = 0; _flight = null;
    }

    public (float X, float Y) ToMap(float screenX, float screenY)
    {
        var (left, top) = Corner;
        return (left + screenX / Scale, top + screenY / Scale);
    }

    // Keep the center where the view can actually be, so a drag past the edge does not
    // build up a distance the camera then has to travel back before moving again.
    private void Clamp()
    {
        var scale = Scale;
        float viewWidth = _width / (float)scale, viewHeight = _height / (float)scale;
        CenterX = Edge(CenterX, viewWidth, mapWidth) + viewWidth / 2;
        CenterY = Edge(CenterY, viewHeight, mapHeight) + viewHeight / 2;
    }
}
