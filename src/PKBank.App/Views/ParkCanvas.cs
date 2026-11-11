using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PKBank.App.Services.Pokepark;
using PKForge.Domain;
using SkiaSharp;

namespace PKBank.App.Views;

/// <summary>
/// The park's window into the world: the Skia scene drawn into a bitmap the Avalonia side
/// shows, ticking with the simulation. The player drags the camera, clicks a resident and
/// scrolls to zoom.
/// </summary>
public sealed class ParkCanvas : Control
{
    private readonly ParkRenderer _renderer;
    private readonly ParkSession _session;
    private readonly DispatcherTimer _timer;
    private WriteableBitmap? _target;
    private SKBitmap? _surface;
    private byte[]? _pixels;
    private bool _dragging;
    private double _lastX, _lastY;

    public ParkCanvas(ParkRenderer renderer, ParkSession session)
    {
        _renderer = renderer;
        _session = session;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ParkLife.TickMilliseconds) };
        _timer.Tick += (_, _) => Frame();
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>The resident the player clicked (the card shows it).</summary>
    public string? SelectedId
    {
        get => _renderer.SelectedId;
        set => _renderer.SelectedId = value;
    }

    /// <summary>Raised when the player picks a resident (or clears the selection).</summary>
    public event Action<string?>? SelectionChanged;

    /// <summary>Raised when the player clicks a point of the world (in map coordinates).</summary>
    public event Action<ParkPoint>? MapClicked;

    /// <summary>Raised while the player drags over the world (in map coordinates).</summary>
    public event Action<ParkPoint>? MapDragged;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _ = _renderer.LoadArtAsync();
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var width = (int)Math.Max(1, Math.Round(finalSize.Width));
        var height = (int)Math.Max(1, Math.Round(finalSize.Height));
        if (_target is null || _target.PixelSize.Width != width || _target.PixelSize.Height != height)
        {
            _target = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Premul);
            _surface?.Dispose();
            _surface = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            _pixels = new byte[width * height * 4];
        }
        return base.ArrangeOverride(finalSize);
    }

    public override void Render(DrawingContext context)
    {
        if (_target is null) return;
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        context.DrawImage(_target, new Rect(0, 0, size.Width, size.Height));
    }

    private void Frame()
    {
        if (_surface is null || _target is null) return;
        var time = Environment.TickCount64;
        _session.Tick(time);
        using (var canvas = new SKCanvas(_surface))
        {
            canvas.Clear(SKColors.Transparent);
            _renderer.Draw(canvas, _surface.Width, _surface.Height, time);
        }
        Blit();
        InvalidateVisual();
    }

    /// <summary>Copies the drawn frame into the Avalonia bitmap (Skia draws RGBA, Avalonia BGRA).</summary>
    private void Blit()
    {
        if (_surface is null || _target is null || _pixels is null) return;
        var source = _surface.Bytes;
        var target = _pixels;
        var count = Math.Min(source.Length, target.Length);
        for (var i = 0; i < count; i += 4)
        {
            target[i] = source[i + 2];
            target[i + 1] = source[i + 1];
            target[i + 2] = source[i];
            target[i + 3] = source[i + 3];
        }
        using var frame = _target.Lock();
        var width = Math.Min(_surface.Width, frame.Size.Width);
        var height = Math.Min(_surface.Height, frame.Size.Height);
        var stride = width * 4;
        for (var row = 0; row < height; row++)
            System.Runtime.InteropServices.Marshal.Copy(target, row * stride,
                frame.Address + row * frame.RowBytes, stride);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetPosition(this);
        Focus();
        var hit = _renderer.HitTest((float)point.X, (float)point.Y);
        if (hit is not null)
        {
            _renderer.SelectedId = hit;
            SelectionChanged?.Invoke(hit);
            MapClicked?.Invoke(_renderer.ScreenToMap((float)point.X, (float)point.Y));
            return;
        }
        _dragging = true;
        _lastX = point.X;
        _lastY = point.Y;
        MapClicked?.Invoke(_renderer.ScreenToMap((float)point.X, (float)point.Y));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var point = e.GetPosition(this);
        _renderer.Viewport.Drag((float)(point.X - _lastX), (float)(point.Y - _lastY));
        MapDragged?.Invoke(_renderer.ScreenToMap((float)point.X, (float)point.Y));
        _lastX = point.X;
        _lastY = point.Y;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        _renderer.Viewport.Zoom(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }
}
