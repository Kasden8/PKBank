using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKBank.App.Theme;

namespace PKBank.App.Views;

/// <summary>The credits and license page.</summary>
public sealed class AboutView : UserControl
{
    public AboutView()
    {
        Background = Brushes.Transparent;

        var lines = new StackPanel { Spacing = 10 };
        lines.Children.Add(Header("ABOUT"));
        lines.Children.Add(Body("PKBank v0.1.0", 14, UiTokens.Ink0));
        lines.Children.Add(Body("A PC pocket storage — every Pokémon you own, in one place. Offline, honest, and yours to keep.", 12.5, UiTokens.Ink1, wrap: true));
        lines.Children.Add(Body("Built on two giants: PKHeX (engine, by Kaphotics and contributors) and PKForge (the Android original this PC port is based on, by sofianeelhor).", 12.5, UiTokens.Ink1, wrap: true));
        lines.Children.Add(Body("Engine: github.com/kwsch/PKHeX · Port base: github.com/sofianeelhor/PKForge", 12, UiTokens.InkSoft));
        lines.Children.Add(Body("Free software, GPL-3.0-or-later. Use it, modify it, share it — keep it free. Full credits and licenses: docs/ATTRIBUTION.md in the repository.", 12, UiTokens.InkSoft, wrap: true));
        lines.Children.Add(Body("Pokémon and Pokémon character names are trademarks of Nintendo, Creatures Inc., and GAME FREAK Inc. Not affiliated.", 11.5, UiTokens.InkSoft, wrap: true));
        lines.Children.Add(Body("No ROM hacks are included. No data leaves this computer unless you say so.", 11.5, UiTokens.InkSoft, wrap: true));

        var panel = new Border
        {
            MaxWidth = 720,
            Margin = new Thickness(30, 26),
            Padding = new Thickness(26, 22),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(12),
            Child = lines,
        };
        Content = new ScrollViewer { Content = panel, HorizontalAlignment = HorizontalAlignment.Center };
    }

    private static TextBlock Header(string text) => PkChrome.PixelLabel(text, 26, UiTokens.Bright);

    private static TextBlock Body(string text, double size, Color color, bool wrap = false) =>
        PkChrome.BodyLabel(text, size, color, wrap);
}
