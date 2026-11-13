using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using PKBank.App.Navigation;
using PKBank.App.Theme;

namespace PKBank.App.Views;

/// <summary>
/// Shown when a screen fails to come up. The app must keep running: the error is logged and
/// explained here instead of taking the whole process down.
/// </summary>
public sealed class ErrorView : UserControl
{
    public ErrorView(string tag, Exception error)
    {
        var headline = PkChrome.PixelLabel($"'{tag}' could not open", 20, UiTokens.Bright);
        var detail = PkChrome.BodyLabel(error.GetType().Name + ": " + error.Message, 13, UiTokens.InkSoft, wrap: true);
        var hint = PkChrome.BodyLabel(
            "The full details are in %AppData%\\PKBank\\logs\\pkbank.log. Nothing was modified — try again, and report the log line if it keeps failing.",
            12.5, UiTokens.InkSoft, wrap: true);

        var back = new Button { Content = "Back to HOME" };
        SaveEditorView.StyleButton(back);
        back.Click += (_, _) => App.Services.GetRequiredService<Navigator>().ShowHome();

        var card = new Border
        {
            Width = 520,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            Child = new StackPanel { Spacing = 12, Children = { headline, detail, hint, back } },
        };

        Content = card;
    }
}
