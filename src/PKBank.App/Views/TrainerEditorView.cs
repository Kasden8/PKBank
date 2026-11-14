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

        _name = TextBoxControl(save.OT);
        _tid = TextBoxControl(save.TrainerTID7.ToString());
        _sid = TextBoxControl(save.TrainerSID7.ToString());
        _money = NumericControl((int)save.Money, 0, 99999999);
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
                Children =
                {
                    LabeledField("Trainer name", _name),
                    LabeledField("TID", _tid),
                    LabeledField("SID", _sid),
                    LabeledField("Money", _money),
                    _status,
                },
            },
        };
        root.Children.Add(panel);

        var apply = new Button { Content = "Apply to save" };
        SaveEditorView.StyleButton(apply);
        apply.Click += (_, _) => Apply();
        root.Children.Add(apply);
        return root;
    }

    /// <summary>A caption plus the field control, laid out as one row. Same rule as the
    /// Pokémon editor: the control is created separately and kept in a field for Apply —
    /// returning the control while it stayed inside the label grid broke the whole view.</summary>
    private static Grid LabeledField(string caption, Control control)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children = { PkChrome.PixelLabel(caption, 12, UiTokens.InkSoft), control },
        };
        Grid.SetColumn(control, 1);
        return grid;
    }

    private static TextBox TextBoxControl(string value) =>
        new() { Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };

    private static NumericUpDown NumericControl(int value, int min, int max) =>
        new() { Value = value, Minimum = min, Maximum = max, ClipValueToMinMax = true, HorizontalAlignment = HorizontalAlignment.Stretch };

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
