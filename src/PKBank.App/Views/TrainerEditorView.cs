using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>The trainer card: name, IDs and money of the save in the editor.</summary>
public sealed class TrainerEditorView : UserControl
{
    private readonly SaveEditorViewModel _vm;
    private TextBox _name = null!;
    private TextBox _tid = null!;
    private TextBox _sid = null!;
    private NumericUpDown _money = null!;
    private TextBlock _status = null!;

    public TrainerEditorView(SaveEditorViewModel vm)
    {
        _vm = vm;
        Background = Brushes.Transparent;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(26, 22) };
        root.Children.Add(PkChrome.PixelLabel("TRAINER", 26, UiTokens.Bright));
        root.Children.Add(PkChrome.BodyLabel("The trainer behind the save in the editor.", 12.5, UiTokens.InkSoft));

        var save = _vm.Save;
        if (save is null)
        {
            root.Children.Add(PkChrome.BodyLabel("Open a save in the editor first.", 13.5, UiTokens.Ink0));
            return root;
        }

        _name = Field("Trainer name", save.OT);
        _tid = Field("TID", save.TrainerTID7.ToString());
        _sid = Field("SID", save.TrainerSID7.ToString());
        _money = Numeric("Money", (int)save.Money, 0, 99999999);
        _status = PkChrome.BodyLabel(save.OT + " · " + save.TrainerIDDisplayFormat, 12, UiTokens.InkSoft);

        var panel = new Border
        {
            Width = 460,
            Padding = new Thickness(18),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Child = new StackPanel
            {
                Spacing = 10,
                Children = { _name, _tid, _sid, _money, _status },
            },
        };
        root.Children.Add(panel);

        var apply = new Button { Content = "Apply to save" };
        SaveEditorView.StyleButton(apply);
        apply.Click += (_, _) => Apply();
        root.Children.Add(apply);
        return root;
    }

    private static TextBox Field(string caption, string value)
    {
        var control = new TextBox { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children = { PkChrome.PixelLabel(caption, 12, UiTokens.InkSoft), control },
        };
        Grid.SetColumn(control, 1);
        return control;
    }

    private static NumericUpDown Numeric(string caption, int value, int min, int max)
    {
        var control = new NumericUpDown { Value = value, Minimum = min, Maximum = max, ClipValueToMinMax = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children = { PkChrome.PixelLabel(caption, 12, UiTokens.InkSoft), control },
        };
        Grid.SetColumn(control, 1);
        return control;
    }

    private void Apply()
    {
        var save = _vm.Save;
        if (save is null) return;
        try
        {
            save.OT = _name.Text ?? string.Empty;
            if (uint.TryParse(_tid.Text, out var tid)) save.TrainerTID7 = tid;
            if (uint.TryParse(_sid.Text, out var sid)) save.TrainerSID7 = sid;
            save.Money = (uint)_money.Value!;
            _vm.Dirty = true;
            _status.Text = "Applied — Save in the editor to persist.";
        }
        catch (Exception error)
        {
            _status.Text = "Could not apply: " + error.Message;
        }
    }
}
