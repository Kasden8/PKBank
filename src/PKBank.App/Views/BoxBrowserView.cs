using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>
/// Every box of the open save on one screen: click a box to jump straight to it in the
/// editor. The PC answer to PKForge's box browser — no more stepping with &lt; / &gt;.
/// </summary>
public sealed class BoxBrowserView : UserControl
{
    private const int Columns = 6;
    private const int Rows = 5;
    private const int CellSize = 32;
    private const int TileWidth = Columns * CellSize + 24;

    private readonly SaveEditorViewModel _vm;
    private readonly SpriteService _sprites;
    private readonly WrapPanel _tiles = new();

    public BoxBrowserView(SaveEditorViewModel vm, SpriteService sprites)
    {
        _vm = vm;
        _sprites = sprites;
        Background = Brushes.Transparent;

        var title = new StackPanel { Spacing = 4 };
        title.Children.Add(PkChrome.PixelLabel("ALL BOXES", 24, UiTokens.Bright));
        title.Children.Add(PkChrome.BodyLabel(
            vm.HasSave
                ? $"{vm.FileName} · {vm.Save!.OT} · {vm.BoxTotal} boxes — click one to open it in the editor."
                : "Open a save first.",
            12.5, UiTokens.InkSoft));

        var back = new Button { Content = "Back to editor" };
        SaveEditorView.StyleButton(back);
        back.Click += (_, _) => Navigator()?.Show(new SaveEditorView(_vm, _sprites));

        var header = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { title, back },
        };
        Grid.SetColumn(back, 1);

        _tiles.Orientation = Orientation.Horizontal;
        var scroller = new ScrollViewer { Content = _tiles };

        var root = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(26, 22),
            Children = { header, scroller },
        };
        Content = root;

        if (vm.HasSave)
            for (var box = 0; box < vm.BoxTotal; box++)
                _tiles.Children.Add(BoxTile(box));
    }

    /// <summary>Number of box tiles drawn (smoke tests).</summary>
    internal int TileCount => _tiles.Children.Count;

    /// <summary>Selects a box and builds the editor already pointing at it (smoke tests).</summary>
    internal Control BuildEditorFor(int index)
    {
        _vm.BoxIndex = Math.Clamp(index, 0, Math.Max(0, _vm.BoxTotal - 1));
        return new SaveEditorView(_vm, _sprites);
    }

    internal void ChooseBox(int index) => Navigator()?.Show(BuildEditorFor(index));

    private Control BoxTile(int box)
    {
        var save = _vm.Save!;
        var slots = Math.Min(save.BoxSlotCount, Columns * Rows);
        var name = (save as IBoxDetailNameRead)?.GetBoxName(box) ?? "";
        if (string.IsNullOrWhiteSpace(name)) name = $"BOX {box + 1}";

        var canvas = new Canvas { Width = Columns * CellSize, Height = Rows * CellSize };
        var filled = 0;
        for (var slot = 0; slot < slots; slot++)
        {
            var pk = _vm.Slot(box, slot);
            if (pk.Species != 0) filled++;
            var cell = new Border
            {
                Width = CellSize - 2,
                Height = CellSize - 2,
                Background = PkChrome.Brush(UiTokens.SlotFill),
                BorderBrush = PkChrome.Brush(UiTokens.SlotEdge),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(1),
                Child = pk.Species == 0 ? null : SpriteThumb(pk),
            };
            Canvas.SetLeft(cell, (slot % Columns) * CellSize + 1);
            Canvas.SetTop(cell, (slot / Columns) * CellSize + 1);
            canvas.Children.Add(cell);
        }

        var caption = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            ColumnSpacing = 6,
            Children =
            {
                PkChrome.PixelLabel(name, 11, UiTokens.Bright),
                PkChrome.BodyLabel($"{filled}/{slots}", 10.5, UiTokens.InkSoft),
            },
        };
        Grid.SetColumn(caption.Children[1], 1);

        var body = new StackPanel { Spacing = 6, Children = { caption, canvas } };
        var current = box == _vm.BoxIndex;
        var tile = new Border
        {
            Width = TileWidth,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(8),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(current ? UiTokens.Bright : UiTokens.Rim),
            BorderThickness = new Thickness(current ? 2 : 1.2),
            CornerRadius = new CornerRadius(8),
            Child = body,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        tile.PointerPressed += (_, _) => ChooseBox(box);
        tile.PointerEntered += (_, _) => tile.BorderBrush = PkChrome.Brush(UiTokens.Bright);
        tile.PointerExited += (_, _) => tile.BorderBrush = PkChrome.Brush(current ? UiTokens.Bright : UiTokens.Rim);
        return tile;
    }

    private Control SpriteThumb(PKM pk)
    {
        var look = new SpriteLook(pk.Species, pk.Form, pk.IsShiny, new SpriteTraits
        {
            Female = pk.Gender == 1,
            FormArgument = (int)((pk as IFormArgument)?.FormArgument ?? 0),
            Gigantamax = false,
        });
        var image = new Image { Source = _sprites.GetSprite(look), Stretch = Stretch.Uniform, Height = CellSize - 6 };
        if (image.Source is null)
        {
            _sprites.Warm(look, () =>
            {
                var ready = _sprites.GetSprite(look);
                if (ready is not null) Dispatcher.UIThread.Post(() => image.Source = ready);
            });
        }
        return image;
    }

    private static Navigation.Navigator? Navigator() =>
        App.Services?.GetService(typeof(Navigation.Navigator)) as Navigation.Navigator;
}
