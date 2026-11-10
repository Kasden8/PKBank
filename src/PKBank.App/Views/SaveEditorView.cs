using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PKForge.Domain;
using PKBank.App.Services;
using PKBank.App.Theme;
using Microsoft.Extensions.DependencyInjection;
using PKBank.App.ViewModels;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>
/// The save editor: open a save, walk its boxes, drag Pokémon between slots.
/// The grid and the summary are rendered from the Core data — no Skia canvas needed.
/// </summary>
public sealed class SaveEditorView : UserControl
{
    private const int CellSize = 108;
    private const int Columns = 6;

    private readonly SaveEditorViewModel _vm;
    private readonly SpriteService _sprites;

    private readonly TextBlock _fileLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _statusLabel = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _saveButton = new() { Content = "Save" };
    private readonly TextBlock _boxTitle = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _boxCounter = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    private Canvas? _partyCanvas;
    private Canvas? _boxCanvas;
    private Border? _summaryHost;

    private static readonly (bool IsParty, int Box, int Slot)[] AllBoxCells =
        Enumerable.Range(0, 30).Select(i => (false, 0, i)).ToArray();
    private static readonly (bool IsParty, int Box, int Slot)[] AllPartyCells =
        Enumerable.Range(0, 6).Select(i => (true, 0, i)).ToArray();

    private readonly Dictionary<(bool IsParty, int Box, int Slot), Border> _cells = new();
    private readonly Dictionary<(bool IsParty, int Box, int Slot), Ellipse> _dots = new();

    private (bool IsParty, int Box, int Slot)? _dragFrom;
    private Border? _ghost;
    private (bool IsParty, int Box, int Slot)? _selected;

    public SaveEditorView(SaveEditorViewModel vm, SpriteService sprites)
    {
        _vm = vm;
        _sprites = sprites;

        _fileLabel.Text = "No save open";
        _fileLabel.Foreground = PkChrome.Brush(UiTokens.Ink0);
        _fileLabel.FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12");
        _fileLabel.FontSize = 15;
        _fileLabel.TextTrimming = TextTrimming.CharacterEllipsis;

        _statusLabel.Foreground = PkChrome.Brush(UiTokens.InkSoft);
        _statusLabel.FontSize = 11.5;

        _saveButton.Click += async (_, _) =>
        {
            var (ok, message) = await _vm.SaveAsync();
            _statusLabel.Text = message;
            _saveButton.IsEnabled = _vm.Dirty && !false;
        };
        StyleButton(_saveButton);

        var openButton = new Button { Content = "Open save" };
        StyleButton(openButton);
        openButton.Click += (_, _) => { _ = PickAndLoadAsync(); };

        var bagButton = new Button { Content = "Bag" };
        StyleButton(bagButton);
        bagButton.Click += (_, _) => Navigator().Show(new BagEditorView(_vm));
        var trainerButton = new Button { Content = "Trainer" };
        StyleButton(trainerButton);
        trainerButton.Click += (_, _) => Navigator().Show(new TrainerEditorView(_vm));

        var top = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            Children = { TopRow(openButton, bagButton, trainerButton) },
        };

        var left = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Top };
        left.Children.Add(LevelLabel("PARTY", reduced: true));
        _partyCanvas = new Canvas { Width = Columns * CellSize, Height = CellSize, Background = Brushes.Transparent };
        left.Children.Add(_partyCanvas);
        left.Children.Add(LevelLabel("BOX"));
        left.Children.Add(BoxRow());
        _boxCanvas = new Canvas { Width = Columns * CellSize, Height = 5 * CellSize, Background = Brushes.Transparent };
        left.Children.Add(_boxCanvas);
        left.Children.Add(HorizontalDivider());

        _summaryHost = new Border
        {
            Width = 292,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(18, 0, 0, 0),
            Padding = new Thickness(14),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
        };
        left.Margin = new Thickness(22);

        var body = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children =
            {
                left,
                _summaryHost,
            },
        };
        Grid.SetColumn(_summaryHost, 1);

        var root = new StackPanel { Spacing = 10, Margin = new Thickness(26, 22), Children = { top, body } };
        Content = root;
        root.Children.Add(_statusLabel);

        RegisterGrid(_partyCanvas, AllPartyCells, party: true);
        RegisterGrid(_boxCanvas, AllBoxCells, party: false);
        RefreshAll();
    }

    private Control TopRow(Button openButton, Button bagButton, Button trainerButton)
    {
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(_saveButton);
        right.Children.Add(openButton);
        right.Children.Add(bagButton);
        right.Children.Add(trainerButton);
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { _fileLabel, right },
        };
        Grid.SetColumn(right, 1);
        return grid;
    }

    internal PKBank.App.Navigation.Navigator Navigator() => App.Services.GetRequiredService<PKBank.App.Navigation.Navigator>()!;

    internal static void StyleButton(Button button)
    {
        button.FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12");
        button.FontSize = 13;
        button.Foreground = PkChrome.Brush(UiTokens.Bright);
        button.Background = PkChrome.ButtonGradient();
        button.BorderBrush = PkChrome.Brush(UiTokens.ButtonEdge);
        button.BorderThickness = new Thickness(1.5);
        button.CornerRadius = new CornerRadius(UiTokens.ControlRadius);
        button.Padding = new Thickness(16, 7);
    }

    private static TextBlock LevelLabel(string caption, bool reduced = false) =>
        PkChrome.PixelLabel(caption, reduced ? 10 : 12, UiTokens.InkSoft);

    private static Control HorizontalDivider() => new Border
    {
        Background = PkChrome.Brush(UiTokens.Divider),
        Height = 1,
        Margin = new Thickness(0, 4, 0, 0),
    };

    private Control BoxRow()
    {
        var back = new Button { Content = "<" };
        var next = new Button { Content = ">" };
        StyleButton(back); StyleButton(next);
        _boxCounter.FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12");
        _boxCounter.FontSize = 12;
        _boxCounter.Foreground = PkChrome.Brush(UiTokens.Ink0);
        _boxTitle.FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12");
        _boxTitle.FontSize = 14;
        _boxTitle.Foreground = PkChrome.Brush(UiTokens.Bright);
        back.Click += (_, _) => StepBox(-1);
        next.Click += (_, _) => StepBox(1);
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { back, _boxTitle, next },
        };
        Grid.SetColumn(_boxTitle, 1);
        Grid.SetColumn(next, 2);
        return grid;
    }

    private void StepBox(int delta)
    {
        if (_vm.Save is null) return;
        _vm.BoxIndex = Math.Clamp(_vm.BoxIndex + delta, 0, Math.Max(0, _vm.BoxTotal - 1));
        RefreshBox();
    }

    /// <summary>Creates the fixed cells for a grid region (party or box area) with pointer handlers.</summary>
    private void RegisterGrid(Canvas canvas, (bool IsParty, int Box, int Slot)[] cells, bool party)
    {
        foreach (var cell in cells)
        {
            var border = new Border
            {
                Width = CellSize,
                Height = CellSize,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4),
            };
            border.Tag = cell;
            border.PointerPressed += (_, e) => OnCellPressed(cell, e);
            border.DoubleTapped += (_, _) => OnCellDoubleTapped(cell);
            Canvas.SetLeft(border, (cell.Slot % Columns) * CellSize);
            Canvas.SetTop(border, (cell.Slot / Columns) * CellSize);
            canvas.Children.Add(border);
            _cells[cell] = border;
            if (!party)
            {
                var dot = new Ellipse { Width = 10, Height = 10, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) };
                _dots[cell] = dot;
            }
        }
        canvas.PointerMoved += (_, e) => OnDragMove(canvas, e);
        canvas.PointerReleased += (_, e) => OnDragRelease(canvas, e);
        canvas.PointerExited += (_, _) => CancelDrag(canvas);
    }

    private void OnCellPressed((bool IsParty, int Box, int Slot) cell, PointerEventArgs e)
    {
        if (_vm.Save is null) return;
        var pk = ReadSlot(cell);
        if (pk.Species == 0)
        {
            _selected = null;
            RefreshSummary();
            PaintSelection();
            return;
        }
        _selected = cell;
        RefreshSummary();
        PaintSelection();
        _dragFrom = cell;
    }

    private void OnCellDoubleTapped((bool IsParty, int Box, int Slot) cell)
    {
        if (_vm.Save is null) return;
        var pk = ReadSlot(cell);
        if (pk.Species == 0) return;
        OpenEditor(pk, cell);
    }

    private void OpenEditor(PKM pk, (bool IsParty, int Box, int Slot) cell)
    {
        var navigator = App.Services.GetRequiredService<PKBank.App.Navigation.Navigator>()!;
        navigator.Show(new PkmEditorView(_vm, _sprites, pk, cell));
    }

    private void ShowQr()
    {
        if (_selected is { } selected)
        {
            var pk = ReadSlot(selected);
            var title = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English) + " · Lv. " + pk.CurrentLevel;
            var dialog = new ShowdownQrView(title, ShowdownParsing.GetShowdownText(pk));
            dialog.HorizontalAlignment = HorizontalAlignment.Center;
            dialog.VerticalAlignment = VerticalAlignment.Center;
            var scrim = new Border { Child = dialog, Background = PkChrome.Brush(PKBank.App.Theme.UiTokens.Scrim), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            scrim.PointerPressed += (_, _) => { dialog.IsVisible = false; scrim.IsVisible = false; };
            Content = scrim;
        }
    }

    private PKM ReadSlot((bool IsParty, int Box, int Slot) cell) =>
        cell.IsParty ? _vm.PartySlot(cell.Slot) : _vm.Slot(_vm.BoxIndex, cell.Slot);

    private static int FormArgumentOf(PKM pk) => (int)((pk as IFormArgument)?.FormArgument ?? 0);

    private void OnDragMove(Canvas canvas, PointerEventArgs e)
    {
        if (_dragFrom is null) return;
        EnsureGhost(canvas, _dragFrom.Value);
        var pos = e.GetCurrentPoint(canvas).Position;
        Canvas.SetLeft(_ghost!, pos.X - 40);
        Canvas.SetTop(_ghost!, pos.Y - 40);
    }

    private void EnsureGhost(Canvas canvas, (bool IsParty, int Box, int Slot) from)
    {
        if (_ghost is not null) return;
        var pk = ReadSlot(from);
        var sprite = _sprites.GetSprite(new SpriteLook(pk.Species, pk.Form, pk.IsShiny, new SpriteTraits
        {
            Female = pk.Gender == 1,
            FormArgument = FormArgumentOf(pk),
            Gigantamax = false,
        }));
        _ghost = new Border
        {
            Width = 56,
            Height = 56,
            Padding = new Thickness(2),
            Background = PkChrome.Brush(UiTokens.Well),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(6),
            Child = sprite is null
                ? new TextBlock { Text = "-", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                : new Image { Source = sprite, Stretch = Stretch.Uniform },
            ZIndex = 100,
        };
        canvas.Children.Add(_ghost);
    }

    private void OnDragRelease(Canvas canvas, PointerEventArgs e)
    {
        if (_dragFrom is null) return;
        RemoveGhost(canvas);
        var from = _dragFrom.Value;
        var idx = CellFromPointer(canvas, e);
        _dragFrom = null;
        if (idx is { } i && i != from.Slot && !false)
        {
            // Within-region move for the slice; cross-region (party <-> box) arrives with the partner screens.
            _vm.Move(from.Box, from.Slot, from.IsParty, from.Box, i, from.IsParty);
        }
        RefreshAll();
    }

    private void RemoveGhost(Canvas? canvas)
    {
        if (_ghost is null) return;
        canvas?.Children.Remove(_ghost);
        _ghost = null;
    }

    private void CancelDrag(Canvas canvas)
    {
        RemoveGhost(canvas);
        _dragFrom = null;
    }

    private int? CellFromPointer(Canvas canvas, PointerEventArgs e)
    {
        var pos = e.GetCurrentPoint(canvas).Position;
        var col = (int)(pos.X / CellSize);
        var row = (int)(pos.Y / CellSize);
        if (col < 0 || col >= Columns) return null;
        var idx = row * Columns + col;
        return idx < 30 ? idx : null;
    }

    private async Task PickAndLoadAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Pokémon save",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Saves") { Patterns = ["*.sav", "*.dsv", "*.dat", "*.gci", "*.bin", "*.*"] }],
        });
        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        var error = await _vm.LoadAsync(path);
        if (error is not null)
        {
            _statusLabel.Text = error;
            return;
        }
        RefreshAll();
    }

    private void PaintSelection()
    {
        foreach (var (cell, border) in _cells)
        {
            var selected = _selected is { } sel && sel == cell;
            border.BorderBrush = selected ? PkChrome.Brush(UiTokens.Bright) : Brushes.Transparent;
            border.Background = selected ? PkChrome.Brush(UiTokens.RowStripe) : Brushes.Transparent;
        }
    }

    /// <summary>Renders every cell (party first, then the current box) with its sprite and name.</summary>
    private void RefreshAll()
    {
        RefreshCells(AllPartyCells, _partyCanvas!);
        RefreshCells(AllBoxCells, _boxCanvas!);
        _boxCounter.Text = _vm.Save is null ? "" : $"{_vm.BoxIndex + 1}/{_vm.BoxTotal}";
        _boxTitle.Text = _vm.BoxTitle;
        _fileLabel.Text = _vm.HasSave ? _vm.FileName + (false ? " (read-only)" : "") : "No save open";
        _statusLabel.Text = _vm.Status;
        _saveButton.IsEnabled = _vm.Dirty && !false;
        RefreshSummary();
        PaintSelection();
        RunLegalityChecks();
        RefreshWallpaper();
    }

    private void RefreshBox()
    {
        RefreshCells(AllBoxCells, _boxCanvas!);
        _boxCounter.Text = _vm.Save is null ? "" : $"{_vm.BoxIndex + 1}/{_vm.BoxTotal}";
        _boxTitle.Text = _vm.BoxTitle;
        RunLegalityChecks();
        RefreshWallpaper();
    }

    private void RefreshCells((bool IsParty, int Box, int Slot)[] cells, Canvas canvas)
    {
        if (_vm.Save is null)
        {
            foreach (var cell in cells) _cells[cell].Child = null;
            return;
        }
        foreach (var cell in cells)
        {
            var pk = cell.IsParty ? _vm.PartySlot(cell.Slot) : _vm.Slot(_vm.BoxIndex, cell.Slot);
            _cells[cell].Child = CellContent(pk, cell);
        }
    }

    private Control? CellContent(PKM pk, (bool IsParty, int Box, int Slot) cell)
    {
        if (pk.Species == 0) return null;
        var look = new SpriteLook(pk.Species, pk.Form, pk.IsShiny, new SpriteTraits
        {
            Female = pk.Gender == 1,
            FormArgument = FormArgumentOf(pk),
            Cosplay = false,
            Gigantamax = false,
        });
        var sprite = _sprites.GetSprite(look);
        if (sprite is null)
        {
            _sprites.Warm(look, () =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_cells.TryGetValue(cell, out var border))
                        border.Child = CellContent(pk, cell);
                });
            });
        }

        var name = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);
        var text = new TextBlock
        {
            Text = pk.IsEgg ? "EGG" : name,
            FontSize = 10,
            Foreground = PkChrome.Brush(UiTokens.Ink1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        };
        Control spriteControl = sprite is null
            ? new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new Image { Source = sprite, Stretch = Stretch.Uniform, Height = 84 };

        var stack = new StackPanel { Spacing = 0, Children = { spriteControl, text } };
        var grid = new Grid { Children = { stack } };
        if (!cell.IsParty && _dots.TryGetValue(cell, out var dot))
        {
            dot.HorizontalAlignment = HorizontalAlignment.Right;
            dot.VerticalAlignment = VerticalAlignment.Top;
            dot.Margin = new Thickness(0, 4, 4, 0);
            grid.Children.Add(dot);
        }
        return grid;
    }

    private void RefreshWallpaper()
    {
        _boxCanvas!.Background = Brushes.Transparent;
        if (_vm.Save is null) return;
        var index = _vm.Save is IBoxDetailWallpaper wp ? wp.GetBoxWallpaper(_vm.BoxIndex) : 0;
        var name = WallpaperName.GetName(_vm.Save.Version, index);
        var wallpaper = _sprites.GetWallpaper(name);
        if (wallpaper is not null)
        {
            _boxCanvas.Background = new ImageBrush
            {
                Source = wallpaper,
                Stretch = Stretch.UniformToFill,
                Opacity = 0.55,
            };
        }
    }

    private void RunLegalityChecks()
    {
        foreach (var (cell, dot) in _dots)
        {
            dot.IsVisible = false;
            dot.Fill = Brushes.Transparent;
        }
        if (_vm.Save is null) return;
        foreach (var cell in AllBoxCells)
        {
            var pk = _vm.Slot(_vm.BoxIndex, cell.Slot);
            if (pk.Species == 0) continue;
            var dot = _dots[cell];
            dot.IsVisible = true;
            // Legacy "checked" green as placeholder while analyzing.
            Task.Run(() => SaveEditorViewModel.AnalyseLegal(pk.Clone()))
                .ContinueWith(t =>
                {
                    var valid = t.Exception is null && t.Result;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        dot.Fill = valid ? PkChrome.Brush(UiTokens.Ok) : PkChrome.Brush(UiTokens.Bad);
                    });
                }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private void RefreshSummary()
    {
        _summaryHost!.Child = SummaryContent();
    }

    private Control SummaryContent()
    {
        var stack = new StackPanel { Spacing = 8 };
        if (_selected is null || _vm.Save is null || ReadSlot(_selected.Value).Species == 0)
        {
            stack.Children.Add(PkChrome.PixelLabel("SUMMARY", 16, UiTokens.Bright));
            stack.Children.Add(PkChrome.BodyLabel("Click a Pokémon to see its details.", 12.5, UiTokens.InkSoft, wrap: true));
            return stack;
        }

        var pk = ReadSlot(_selected.Value);
        var strings = GameInfo.GetStrings("en");
        var name = SpeciesName.GetSpeciesName(pk.Species, (int)LanguageID.English);

        Row(stack, "SPECIES", pk.IsEgg ? "EGG" : name, 15);
        Row(stack, "LEVEL", $"Lv. {pk.CurrentLevel}", 13.5);
        Row(stack, "GENDER", pk.Gender switch { 0 => "Male", 1 => "Female", _ => "—" }, 12.5);
        Row(stack, "NATURE", NatureUtil.GetNature((Nature)pk.Nature).ToString(), 12.5);
        Row(stack, "ABILITY", $"#{pk.Ability} {SafeName(strings, pk.Ability)}", 12.5);
        Row(stack, "ITEM", pk.HeldItem == 0 ? "—" : SafeName(strings, pk.HeldItem, array: true), 12.5);
        Row(stack, "BALL", SafeName(strings, pk.Ball, array: false), 12.5);
        Row(stack, "OT", $"{pk.OriginalTrainerName} ({pk.TID16:00000}/{pk.SID16:00000})", 12.5);
        Row(stack, "PID", $"0x{pk.PID:X8}", 12);
        Row(stack, "FRIENDSHIP", pk.CurrentFriendship.ToString(), 12.5);
        Row(stack, "IV", string.Join(" ", GetStats(pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD).Select(v => v.ToString("00"))), 13.5);
        Row(stack, "EV", string.Join(" ", EvArray(pk).Select(v => v.ToString("000"))), 12);
        Row(stack, "HP", $"{pk.Stat_HPCurrent}/{pk.Stat_HPMax}", 12.5);

        var movesLabel = PkChrome.PixelLabel("MOVES", 11, UiTokens.InkSoft);
        stack.Children.Add(movesLabel);
        foreach (var move in new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 })
        {
            stack.Children.Add(PkChrome.BodyLabel(move == 0 ? "—" : SafeName(strings, move, type: "move"), 12, UiTokens.Ink0));
        }

        var bankRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        var send = new Button { Content = "→ Bank (move)" };
        StyleButton(send);
        var copy = new Button { Content = "+ Bank (copy)" };
        StyleButton(copy);
        var editButton = new Button { Content = "Edit" };
        StyleButton(editButton);
        var qrButton = new Button { Content = "Showdown QR" };
        StyleButton(qrButton);
        var selected = _selected!.Value;
        editButton.Click += (_, _) =>
        {
            var pkNow = ReadSlot(selected);
            OpenEditor(pkNow, selected);
        };
        qrButton.Click += (_, _) => ShowQr();
        bankRow.Children.Add(editButton);
        bankRow.Children.Add(qrButton);
        send.Click += async (_, _) =>
        {
            var message = await _vm.DepositSelectedAsync(selected.IsParty, selected.Box, selected.Slot, move: true);
            _statusLabel.Text = message;
            RefreshAll();
        };
        copy.Click += async (_, _) =>
        {
            var message = await _vm.DepositSelectedAsync(selected.IsParty, selected.Box, selected.Slot, move: false);
            _statusLabel.Text = message;
            RefreshAll();
        };
        bankRow.Children.Add(send);
        bankRow.Children.Add(copy);
        bankRow.Children.Add(editButton);
        stack.Children.Add(bankRow);
        return stack;
    }

    private static int[] GetStats(int hp, int atk, int def, int spe, int spa, int spd) => [hp, atk, def, spe, spa, spd];

    private static int[] EvArray(PKM pk)
    {
        Span<int> span = stackalloc int[6];
        pk.GetEVs(span);
        return span.ToArray();
    }

    private static string SafeName(GameStrings strings, int id, bool array = false, string type = "")
    {
        try
        {
            return type switch
            {
                "move" => 0 < id && id < strings.Move.Count ? strings.Move[id] : $"#{id}",
                _ => id >= 0 && (array ? id < strings.Item.Count : id < strings.Ability.Count) ? (array ? strings.Item[id] : strings.Ability[id]) : $"#{id}",
            };
        }
        catch (Exception) { return $"#{id}"; }
    }

    private static void Row(StackPanel stack, string caption, string value, double size)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            ColumnSpacing = 10,
            Children =
            {
                PkChrome.PixelLabel(caption, 10, UiTokens.InkSoft),
                PkChrome.BodyLabel(value, size, UiTokens.Ink0),
            },
        };
        Grid.SetColumn(grid.Children[1], 1);
        stack.Children.Add(grid);
    }
}
