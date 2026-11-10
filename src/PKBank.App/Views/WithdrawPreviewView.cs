using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKBank.App.Theme;
using PKBank.App.ViewModels;

namespace PKBank.App.Views;

/// <summary>
/// The withdraw preview: what each selected Pokémon becomes inside the open save
/// (same format vs converted), any risk to the result, and a legality verdict —
/// shown BEFORE anything is written.
/// </summary>
public sealed class WithdrawPreviewView : Border
{
    public event Action? Confirmed;
    public event Action? Cancelled;

    public WithdrawPreviewView(IReadOnlyList<BankViewModel.WithdrawPreviewItem> items, bool copy)
    {
        Width = 560;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Background = PkChrome.PanelGradient();
        BorderBrush = PkChrome.Brush(UiTokens.Rim);
        BorderThickness = new Thickness(1.2);
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(20, 16);

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(PkChrome.PixelLabel((copy ? "COPY TO SAVE" : "SEND TO SAVE") + " —PREVIEW", 16, UiTokens.Bright));
        foreach (var item in items)
        {
            var from = item.FromFormat;
            var to = item.ToFormat;
            var state = item.IsIdentical ? "same format" : $"{from} → {to} ({item.ConversionResult})";
            var lawful = item.IsLegalAfter ? "legal after move" : "check after move";
            var line = PkChrome.BodyLabel($"{item.SpeciesName} · {state} · {lawful}", 12.5, item.IsLegalAfter ? UiTokens.Ink0 : UiTokens.Warn, wrap: true);
            stack.Children.Add(line);
        }
        stack.Children.Add(PkChrome.BodyLabel("Nothing is written until you confirm. The Bank entries stay intact on copy; on send they leave after a successful write. You will save with the restore point afterwards.", 11.5, UiTokens.InkSoft, wrap: true));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var confirm = new Button { Content = copy ? "Copy anyway" : "Send anyway" };
        var cancel = new Button { Content = "Go back" };
        SaveEditorView.StyleButton(confirm);
        SaveEditorView.StyleButton(cancel);
        confirm.Background = PkChrome.Brush(UiTokens.MenuBlue);
        confirm.Click += (_, _) => Confirmed?.Invoke();
        cancel.Click += (_, _) => Cancelled?.Invoke();
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        Child = stack;
    }
}
