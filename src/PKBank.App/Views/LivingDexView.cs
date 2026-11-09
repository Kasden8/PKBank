using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>
/// The living dex tracker: which species you already own (Bank + open save). Read-only grid.
/// </summary>
public sealed class LivingDexView : UserControl
{
    private const int TotalSpecies = 1025;
    private const int Columns = 10;
    private const int CellSize = 74;
    private const int DexStart = 1;

    private readonly SpriteService _sprites;
    private readonly BankViewModel _bank;
    private readonly SaveEditorViewModel? _saveEditorVm;
    private readonly TextBlock _status = new();
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent };
    private Cell[] _cells = [];

    private sealed class Cell
    {
        public readonly int Species;
        public readonly Border Box;
        public Cell(int species)
        {
            Species = species;
            Box = new Border { Width = CellSize, Height = CellSize, CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, BorderBrush = PkChrome.Brush(UiTokens.Well), BorderThickness = new Thickness(1) };
        }
    }

    public LivingDexView(BankViewModel bank, SaveEditorViewModel? saveEditorVm, SpriteService sprites)
    {
        _sprites = sprites;
        _bank = bank;
        _saveEditorVm = saveEditorVm;
        Background = Brushes.Transparent;
        _status.FontSize = 12.5;
        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);

        var root = new StackPanel { Spacing = 12, Margin = new Thickness(26, 22) };
        root.Children.Add(PkChrome.PixelLabel("LIVING DEX", 26, UiTokens.Bright));
        root.Children.Add(PkChrome.BodyLabel("Tracked species owned across the Bank (and the open save, if any). Read-only in this phase; the autopilot comes later.", 12.5, UiTokens.InkSoft));

        var scroll = new ScrollViewer { Content = _canvas };
        root.Children.Add(scroll);
        root.Children.Add(_status);
        Content = root;
        RenderAsync();
    }

    private void RenderAsync()
    {
        var owned = CollectOwned();
        BuildGrid(owned);
        _status.Text = $"{owned.Count}/{TotalSpecies} species owned ({(int)Math.Round(100d * owned.Count / TotalSpecies)}%)";
    }

    private HashSet<int> CollectOwned()
    {
        var set = new HashSet<int>();
        foreach (var entry in _bank.Entries)
            set.Add(entry.Info.Species);
        if (_saveEditorVm?.Save is { } save)
        {
            for (var b = 0; b < save.BoxCount; b++)
                for (var s = 0; s < save.BoxSlotCount; s++)
                {
                    var pk = save.GetBoxSlotAtIndex(b, s);
                    if (pk.Species != 0) set.Add(pk.Species);
                }
        }
        return set;
    }

    private void BuildGrid(HashSet<int> owned)
    {
        var rows = (TotalSpecies / Columns);
        _canvas.Width = Columns * CellSize;
        _canvas.Height = (rows + 1) * CellSize;
        _canvas.Children.Clear();
        _cells = new Cell[TotalSpecies];
        for (var id = 0; id < TotalSpecies; id++)
        {
            var species = DexStart + id;
            var cell = new Cell(species);
            var col = id % Columns;
            var row = id / Columns;
            Canvas.SetLeft(cell.Box, col * CellSize);
            Canvas.SetTop(cell.Box, row * CellSize);
            cell.Box.Child = CellContent(species, owned.Contains(species));
            _canvas.Children.Add(cell.Box);
            _cells[id] = cell;
        }
    }

    private Control CellContent(int species, bool owned)
    {
        var sprite = owned ? _sprites.GetSprite(new SpriteLook(species, 0, false)) : null;
        var name = SpeciesName.GetSpeciesName((ushort)species, (int)LanguageID.English);
        var text = new TextBlock
        {
            Text = owned ? $"{species}" : "?",
            FontSize = 10,
            Foreground = owned ? PkChrome.Brush(UiTokens.Ink0) : PkChrome.Brush(UiTokens.InkSoft),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        };
        Control body;
        if (sprite is not null)
        {
            body = new Image { Source = sprite, Stretch = Stretch.Uniform, Height = 56, Opacity = owned ? 1 : 0.35 };
        }
        else
        {
            body = new TextBlock { Text = "○", FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center, Foreground = PkChrome.Brush(UiTokens.InkSoft) };
        }
        var stack = new StackPanel { Spacing = 0, Children = { body, text } };
        var grid = new Grid { Children = { stack } };
        if (owned)
        {
            var border = new Border
            {
                Width = CellSize * 0.24,
                Height = CellSize * 0.24,
                CornerRadius = new CornerRadius(6),
                Background = PkChrome.Brush(UiTokens.Ok),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Padding = new Thickness(2),
            };
            grid.Children.Add(border);
        }
        return grid;
    }
}
