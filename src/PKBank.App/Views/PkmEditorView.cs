using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using PKBank.App.Navigation;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>
/// The Pokémon detail editor (PKForge's BoxBrowserPage, trimmed): a small form of fields
/// that can be edited and written back into the save slot the Pokémon lives in.
/// </summary>
public sealed class PkmEditorView : UserControl
{
    private readonly SaveEditorViewModel _vm;
    private readonly SpriteService _sprites;
    private readonly PKM _entity;
    private readonly (bool IsParty, int Box, int Slot) _slot;
    private readonly GameStrings _strings;

    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private TextBox _nickBox = null!;
    private NumericUpDown _levelBox = null!;
    private NumericUpDown _formBox = null!;
    private ComboBox _genderBox = null!;
    private ComboBox _natureBox = null!;
    private ComboBox _abilityBox = null!;
    private ComboBox _ballBox = null!;
    private ComboBox _itemBox = null!;
    private ComboBox[] _moves = [];
    private NumericUpDown[] _ivBoxes = [];
    private NumericUpDown[] _evBoxes = [];
    private NumericUpDown _friendshipBox = null!;
    private TextBox _otBox = null!;
    private TextBox _tidBox = null!;
    private TextBox _sidBox = null!;

    public PkmEditorView(SaveEditorViewModel vm, SpriteService sprites, PKM pk, (bool IsParty, int Box, int Slot) slot)
    {
        _vm = vm;
        _sprites = sprites;
        _entity = pk;
        _slot = slot;
        _strings = GameInfo.GetStrings("en");
        Background = Brushes.Transparent;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(26, 22) };

        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(PkChrome.PixelLabel("POKÉMON", 13, UiTokens.InkSoft));
        header.Children.Add(PkChrome.PixelLabel(SpeciesName.GetSpeciesName(_entity.Species, (int)LanguageID.English), 26, UiTokens.Bright));
        header.Children.Add(PkChrome.PixelLabel(SaveEditorViewModel.AnalyseLegal(_entity) ? "LEGAL" : "ILLEGAL", 11, UiTokens.Ok));
        root.Children.Add(header);

        var body = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 16,
        };
        body.Children.Add(SpritePanel());
        var tabs = TabsPanel();
        body.Children.Add(tabs);
        Grid.SetColumn(tabs, 1);
        root.Children.Add(body);

        var save = new Button { Content = "Apply to save slot" };
        var back = new Button { Content = "Back to boxes" };
        SaveEditorView.StyleButton(save);
        SaveEditorView.StyleButton(back);
        save.Click += (_, _) => Apply();
        back.Click += (_, _) => NavigateBack();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(save);
        buttons.Children.Add(back);
        root.Children.Add(buttons);
        root.Children.Add(new TextBlock
        {
            Text = "Applying writes into the slot; use Save in the editor to persist.",
            Foreground = PkChrome.Brush(UiTokens.InkSoft),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return root;
    }

    private Control SpritePanel()
    {
        var look = new SpriteLook(_entity.Species, _entity.Form, _entity.IsShiny, new SpriteTraits
        {
            Female = _entity.Gender == 1,
            Gigantamax = false,
            Cosplay = false,
        });
        var sprite = _sprites.GetSprite(look);
        Control image = sprite is null
            ? new TextBlock { Text = "—", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new Image { Source = sprite, Stretch = Stretch.Uniform, Width = 180, Height = 180 };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(PkChrome.PixelLabel((_entity.IsEgg ? "EGG" : SpeciesName.GetSpeciesName(_entity.Species, (int)LanguageID.English)), 15, UiTokens.Bright));
        stack.Children.Add(image);
        stack.Children.Add(PkChrome.BodyLabel($"Lv. {_entity.CurrentLevel} · {_entity.Gender switch { 0 => "Male", 1 => "Female", _ => "—" }} · {_entity.Context}", 12.5, UiTokens.InkSoft));
        return new Border
        {
            Width = 220,
            Padding = new Thickness(16),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Child = stack,
        };
    }

    private Control TabsPanel()
    {
        var tabControl = new TabControl { TabStripPlacement = Dock.Top, MinHeight = 420 };
        tabControl.Items.Add(new TabItem { Header = "Basic", Content = BasicTab() });
        tabControl.Items.Add(new TabItem { Header = "Moves & stats", Content = MovesTab() });
        tabControl.Items.Add(new TabItem { Header = "Trainer", Content = TrainerTab() });
        return PkChrome.Panel(tabControl, padding: 6);
    }

    private Control BasicTab()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        _nickBox = TextBoxField("Nickname", _entity.Nickname);
        _levelBox = NumericField("Level", _entity.CurrentLevel, 1, 100);
        _formBox = NumericField("Form", _entity.Form, 0, 255);
        _genderBox = ComboBoxField("Gender", ["Male", "Female", "—"], _entity.Gender);
        var shiny = new CheckBox { Content = "Shiny", IsChecked = _entity.IsShiny, IsEnabled = false };
        stack.Children.Add(_nickBox);
        stack.Children.Add(_levelBox);
        stack.Children.Add(_formBox);
        stack.Children.Add(_genderBox);
        stack.Children.Add(shiny);
        return stack;
    }

    private Control MovesTab()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        _natureBox = ComboBoxField("Nature", NatureNames(), (int)_entity.Nature);
        _abilityBox = ComboBoxField("Ability", NameList(_strings.Ability), _entity.Ability);
        _ballBox = ComboBoxField("Ball", NameList(_strings.balllist), _entity.Ball);
        _itemBox = ComboBoxField("Held item", NameList(_strings.Item), _entity.HeldItem);
        _moves =
        [
            ComboBoxField("Move 1", NameList(_strings.Move), _entity.Move1),
            ComboBoxField("Move 2", NameList(_strings.Move), _entity.Move2),
            ComboBoxField("Move 3", NameList(_strings.Move), _entity.Move3),
            ComboBoxField("Move 4", NameList(_strings.Move), _entity.Move4),
        ];
        stack.Children.Add(_natureBox);
        stack.Children.Add(_abilityBox);
        stack.Children.Add(_ballBox);
        stack.Children.Add(_itemBox);
        foreach (var move in _moves) stack.Children.Add(move);

        var ivRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var evRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        _ivBoxes = MakeStatRow(ivRow, "IV", _entity.IV_HP, _entity.IV_ATK, _entity.IV_DEF, _entity.IV_SPE, _entity.IV_SPA, _entity.IV_SPD, 0, 31);
        var evs = new int[6];
        _entity.GetEVs(evs);
        _evBoxes = MakeStatRow(evRow, "EV", evs[0], evs[1], evs[2], evs[3], evs[4], evs[5], 0, 255);
        stack.Children.Add(ivRow);
        stack.Children.Add(evRow);
        _friendshipBox = NumericField("Friendship", _entity.CurrentFriendship, 0, 255);
        stack.Children.Add(_friendshipBox);
        return stack;
    }

    private Control TrainerTab()
    {
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(10) };
        _otBox = TextBoxField("Original trainer", _entity.OriginalTrainerName);
        _tidBox = TextBoxField("TID", _entity.TID16.ToString());
        _sidBox = TextBoxField("SID", _entity.SID16.ToString());
        stack.Children.Add(_otBox);
        stack.Children.Add(_tidBox);
        stack.Children.Add(_sidBox);
        stack.Children.Add(PkChrome.BodyLabel("Changing these changes the OT/ID that other games will see.", 11.5, UiTokens.InkSoft, wrap: true));
        return stack;
    }

    private static string[] NatureNames() => [.. Enum.GetNames<Nature>()];

    private static string[] NameList(IReadOnlyList<string> names) => [.. names];

    private static NumericUpDown NumericField(string caption, int value, int min, int max)
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

    private static TextBox TextBoxField(string caption, string value)
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

    private static ComboBox ComboBoxField(string caption, string[] options, int selected)
    {
        var control = new ComboBox { ItemsSource = options, SelectedIndex = Math.Clamp(selected, 0, Math.Max(0, options.Length - 1)), HorizontalAlignment = HorizontalAlignment.Stretch };
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children = { PkChrome.PixelLabel(caption, 12, UiTokens.InkSoft), control },
        };
        Grid.SetColumn(control, 1);
        return control;
    }

    private static NumericUpDown[] MakeStatRow(StackPanel row, string caption, int a, int b, int c, int d, int e, int f, int min, int max)
    {
        row.Children.Add(PkChrome.PixelLabel(caption, 11, UiTokens.InkSoft));
        var boxes = new NumericUpDown[6];
        var values = new[] { a, b, c, d, e, f };
        for (var i = 0; i < 6; i++)
        {
            var box = new NumericUpDown { Value = values[i], Minimum = min, Maximum = max, ClipValueToMinMax = true, Width = 52 };
            boxes[i] = box;
            row.Children.Add(box);
        }
        return boxes;
    }

    private void Apply()
    {
        var pk = _entity.Clone();
        var saved = _vm.Save;
        if (saved is null) { _status.Text = "No save open."; return; }
        try
        {
            pk.Nickname = _nickBox.Text ?? string.Empty;
            pk.CurrentLevel = (byte)_levelBox.Value!;
            pk.Form = (byte)_formBox.Value!;
            pk.Gender = (byte)_genderBox.SelectedIndex;
            pk.Nature = (Nature)(byte)Math.Clamp(_natureBox.SelectedIndex, 0, Enum.GetNames<Nature>().Length - 1);
            pk.Ability = _abilityBox.SelectedIndex;
            pk.Ball = (byte)_ballBox.SelectedIndex;
            pk.HeldItem = _itemBox.SelectedIndex;
            pk.Move1 = (ushort)_moves[0].SelectedIndex;
            pk.Move2 = (ushort)_moves[1].SelectedIndex;
            pk.Move3 = (ushort)_moves[2].SelectedIndex;
            pk.Move4 = (ushort)_moves[3].SelectedIndex;
            pk.IV_HP = (int)_ivBoxes[0].Value!;
            pk.IV_ATK = (int)_ivBoxes[1].Value!;
            pk.IV_DEF = (int)_ivBoxes[2].Value!;
            pk.IV_SPE = (int)_ivBoxes[3].Value!;
            pk.IV_SPA = (int)_ivBoxes[4].Value!;
            pk.IV_SPD = (int)_ivBoxes[5].Value!;
            Span<int> evs = stackalloc int[6];
            for (var i = 0; i < 6; i++) evs[i] = (int)_evBoxes[i].Value!;
            pk.SetEVs(evs);
            pk.CurrentFriendship = (byte)_friendshipBox.Value!;
            if (ushort.TryParse(_tidBox.Text, out var tid)) pk.TID16 = tid;
            if (ushort.TryParse(_sidBox.Text, out var sid)) pk.SID16 = sid;
            pk.OriginalTrainerName = _otBox.Text ?? string.Empty;
        }
        catch (Exception error)
        {
            _status.Text = "Some field could not be applied: " + error.Message;
            return;
        }

        if (_slot.IsParty) saved.SetPartySlotAtIndex(pk, _slot.Slot, EntityImportSettings.None);
        else saved.SetBoxSlotAtIndex(pk, _slot.Box, _slot.Slot, EntityImportSettings.None);
        _vm.Dirty = true;

        var la = SaveEditorViewModel.AnalyseLegal(pk);
        _status.Text = la
            ? "Applied to the save slot — Save to persist."
            : "Applied — but flagged illegal afterward (recent gen-3 core issue, see D22).";
    }

    private void NavigateBack()
    {
        var navigator = App.Services.GetRequiredService<Navigator>()!;
        navigator.ShowHome();
    }
}

