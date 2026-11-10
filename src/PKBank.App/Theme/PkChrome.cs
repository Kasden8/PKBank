using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PKBank.App.Theme;

/// <summary>Reusable bits of the PKBank visual language, shared by the shell and the placeholder screens.</summary>
public static class PkChrome
{
    public static SolidColorBrush Brush(Color color) => new(color);

    public static LinearGradientBrush VerticalGradient(Color top, Color bottom) => new()
    {
        GradientStops =
        [
            new GradientStop(top, 0f),
            new GradientStop(bottom, 1f),
        ],
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
    };

    public static LinearGradientBrush ButtonGradient() => new()
    {
        GradientStops =
        [
            new GradientStop(Color.Parse("#3A3F5C"), 0f),
            new GradientStop(Color.Parse("#23263B"), 1f),
        ],
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
    };

    public static LinearGradientBrush PanelGradient() => new()
    {
        GradientStops =
        [
            new GradientStop(UiTokens.PanelTop, 0f),
            new GradientStop(UiTokens.Well, 0.12f),
            new GradientStop(UiTokens.Well, 1f),
        ],
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
    };

    public static LinearGradientBrush StripGradient() => VerticalGradient(UiTokens.StripTop, UiTokens.StripBottom);

    public static Border Panel(Control content, double padding = 12) =>
        new()
        {
            Background = PanelGradient(),
            BorderBrush = Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Padding = new Thickness(padding),
            Child = content,
        };

    public static void SetRowFocus(Border row, bool focused, bool shaded = false)
    {
        row.Background = focused
            ? Brush(UiTokens.SelectFill)
            : shaded
                ? Brush(UiTokens.RowStripe)
                : Brushes.Transparent;
        row.BorderBrush = focused ? Brush(UiTokens.Rim) : Brushes.Transparent;
    }

    public static TextBlock PixelLabel(string text, double size, Color color, bool center = false) =>
        new()
        {
            Text = text,
            FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12"),
            FontSize = size,
            Foreground = Brush(color),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    public static TextBlock BodyLabel(string text, double size, Color color, bool wrap = false) =>
        new()
        {
            Text = text,
            FontSize = size,
            Foreground = Brush(color),
            TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
}
