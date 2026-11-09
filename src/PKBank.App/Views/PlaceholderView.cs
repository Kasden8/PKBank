using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKBank.App.Theme;

namespace PKBank.App.Views;

/// <summary>A quiet placeholder for a screen that lands in a later phase.</summary>
public sealed class PlaceholderView : UserControl
{
    public PlaceholderView(string title, string detail, string phase)
    {
        Background = Brushes.Transparent;

        var titleLabel = PkChrome.PixelLabel(title, 26, UiTokens.Bright, center: true);
        var detailLabel = PkChrome.BodyLabel(detail, 13.5, UiTokens.Ink1, wrap: true);
        detailLabel.MaxWidth = 480;
        detailLabel.TextAlignment = TextAlignment.Center;
        detailLabel.HorizontalAlignment = HorizontalAlignment.Center;

        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 3),
            Background = PkChrome.Brush(UiTokens.MenuBlue),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1),
            Child = PkChrome.PixelLabel(phase, 11, UiTokens.Bright, center: true),
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel
        {
            Spacing = 18,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { titleLabel, detailLabel, chip },
        };
        Content = stack;
    }
}
