using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
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
/// The Bank: the player's whole collection, HOME-style. Grid of the current box with
/// search, basic reordering (drag), and send/receive plumbing to the open save.
/// </summary>
public sealed class BankView : UserControl
{
    private const int CellSize = 108;
    private const int Columns = 6;

    private static readonly (string Id, string Label)[] Filters = [("all", "ALL"), ("shiny", "SHINY"), ("legendary", "SPHINX"), ("egg", "EGG")];

    private readonly BankViewModel _vm;
    private readonly SpriteService _sprites;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _summaryText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _searchBox = new() { MinWidth = 200, VerticalAlignment = VerticalAlignment.Center };
    private readonly Canvas _canvas = new() { Width = Columns * CellSize, Height = 5 * CellSize, Background = Brushes.Transparent };
    private readonly Grid _host = new();
    private readonly Dictionary<(int Box, int Slot), Border> _cells = new();
    private readonly StackPanel _chipRow = new() { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };

    private (int Box, int Slot)? _dragFrom;
    private (int Box, int Slot)? _selected;
    private Border? _ghost;
    private readonly TextBlock _boxCounter = new() { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    public BankView(BankViewModel vm, SpriteService sprites)
    {
        _vm = vm;
        _sprites = sprites;

        _searchBox.PlaceholderText = "Search name / nickname…";
        _searchBox.TextChanged += (_, _) => RefreshGrid();
        _searchBox.Margin = new Thickness(0, 0, 8, 0);
        StyleText(_searchBox);

        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);
        _status.FontSize = 12.5;
        _summaryText.Foreground = PkChrome.Brush(UiTokens.InkSoft);
        _summaryText.FontSize = 12.5;

        var topRow = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { TopStrip() } };

        var left = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(22, 0, 0, 0) };
        left.Children.Add(LevelLabel("BOX"));
        left.Children.Add(BoxNavRow());
        left.Children.Add(_canvas);

        var summaryHost = new Border
        {
            Width = 292,
            VerticalAlignment = VerticalAlignment.Stretch,
            Margin = new Thickness(18, 0, 0, 0),
            Padding = new Thickness(14),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Child = SummarySlot(),
        };

        var body = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star)],
            Children = { left, summaryHost },
        };
        Grid.SetColumn(summaryHost, 1);

        var root = new StackPanel { Spacing = 10, Margin = new Thickness(26, 22), Children = { topRow, body, _status } };
        _host.Children.Add(root);
        Content = _host;
        RegisterCells();
        _canvas.PointerMoved += (_, e) => OnDragMove(e);
        _canvas.PointerReleased += (_, e) => OnDragRelease(e);
        _canvas.PointerExited += (_, _) => CancelDrag();
        RefreshGrid();
    }

    private Control TopStrip()
    {
        var title = PkChrome.PixelLabel("THE BANK", 18, UiTokens.Bright);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var withdraw = MakeButton("Withdraw to save");
        withdraw.Click += async (_, _) => await WithdrawWithPreviewAsync(copy: false);
        var withdrawCopy = MakeButton("Copy to save");
        withdrawCopy.Click += async (_, _) => await WithdrawWithPreviewAsync(copy: true);
        var deposit = MakeButton("Export .pk folder");
        deposit.Click += async (_, _) => _status.Text = await ExportFolderAsync();
        var import = MakeButton("Import .pk folder");
        import.Click += async (_, _) => _status.Text = await ImportFolderAsync();
        var addBox = MakeButton("+ Box");
        addBox.Click += (_, _) => { _vm.BankAddBox(); RefreshGrid(); };
        var remove = MakeButton("Remove");
        remove.Click += (_, _) => { if (_selected is { } sel && _vm.Entry(sel.Box, sel.Slot) is { } e) { _vm.Remove(e.Id); RefreshGrid(); } };
        right.Children.Add(withdraw);
        right.Children.Add(withdrawCopy);
        right.Children.Add(deposit);
        right.Children.Add(import);
        right.Children.Add(addBox);
        right.Children.Add(remove);

        var searchRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        searchRow.Children.Add(_searchBox);
        foreach (var (id, label) in Filters)
        {
            var chip = MakeButton(label);
            chip.Tag = id;
            chip.Click += (_, _) => SetKindFilter(id);
            _chipRow.Children.Add(chip);
        }
        searchRow.Children.Add(_chipRow);

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { title, right },
        };
        Grid.SetColumn(right, 1);

        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Children = { grid, searchRow } };
        return rows;
    }

    private IReadOnlyList<Guid> SelectedIds() => _selected is { } s && _vm.Entry(s.Box, s.Slot) is { } e ? [e.Id] : [];

    private async Task WithdrawWithPreviewAsync(bool copy)
    {
        var ids = SelectedIds();
        if (ids.Count == 0)
        {
            _status.Text = "Select a Pokémon first.";
            return;
        }
        if (_vm.CurrentSaveProvider?.Invoke()?.Save is null)
        {
            _status.Text = "No save open — open a save before withdrawing.";
            return;
        }
        var items = _vm.PreviewWithdraw(ids);
        if (items.Count == 0)
        {
            _status.Text = "Nothing readable to move.";
            return;
        }
        var dialog = new WithdrawPreviewView(items, copy);
        var scrim = new Border
        {
            Background = PkChrome.Brush(UiTokens.Scrim),
            Child = dialog,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        dialog.Confirmed += () =>
        {
            _host.Children.Remove(scrim);
            _ = WithdrawRunAsync(ids, copy);
        };
        dialog.Cancelled += () => _host.Children.Remove(scrim);
        scrim.PointerPressed += (_, _) => _host.Children.Remove(scrim);
        _host.Children.Add(scrim);
    }

    private async Task WithdrawRunAsync(IReadOnlyList<Guid> ids, bool copy)
    {
        _status.Text = await _vm.WithdrawAsync(ids, copy);
        RefreshGrid();
    }

    private void SetKindFilter(string id)
    {
        _vm.KindFilter = id;
        RefreshGrid();
    }

    private static void StyleText(TextBox text)
    {
        text.FontSize = 12.5;
        text.Foreground = PkChrome.Brush(UiTokens.Ink0);
        text.Background = PkChrome.Brush(UiTokens.Well);
        text.BorderBrush = PkChrome.Brush(UiTokens.Divider);
        text.BorderThickness = new Thickness(1);
        text.CornerRadius = new CornerRadius(UiTokens.ControlRadius);
    }

    private static Button MakeButton(string label)
    {
        var button = new Button { Content = label };
        SaveEditorView.StyleButton(button);
        button.FontSize = 12.5;
        return button;
    }

    private static TextBlock LevelLabel(string caption) => PkChrome.PixelLabel(caption, 12, UiTokens.InkSoft);

    private Control BoxNavRow()
    {
        var back = MakeButton("<");
        var next = MakeButton(">");
        back.Click += (_, _) => StepBox(-1);
        next.Click += (_, _) => StepBox(1);
        _boxCounter.FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12");
        _boxCounter.FontSize = 12;
        _boxCounter.Foreground = PkChrome.Brush(UiTokens.Ink0);
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { back, _boxCounter, next },
        };
        Grid.SetColumn(_boxCounter, 1);
        Grid.SetColumn(next, 2);
        return grid;
    }

    private void StepBox(int delta)
    {
        _vm.BoxIndex = Math.Clamp(_vm.BoxIndex + delta, 0, Math.Max(0, _vm.BoxTotal - 1));
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        _boxCounter.Text = _vm.BoxTotal == 0 ? "" : $"{_vm.BoxIndex + 1}/{_vm.BoxTotal}";
        foreach (var (cell, border) in _cells)
        {
            var entry = _vm.Entry(cell.Box, cell.Slot);
            border.Child = entry is null ? null : CellContent(entry);
        }
        RefreshSummary();
        _status.Text = _vm.Status;
    }

    private Control? CellContent(BankEntry entry)
    {
        var look = entry.Info.Look;
        var sprite = _sprites.GetSprite(look);
        if (sprite is null)
            _sprites.Warm(look, () => Dispatcher.UIThread.Post(() =>
            {
                if (_cells.TryGetValue((entry.Box, entry.Slot), out var border))
                    border.Child = CellContent(entry);
            }));

        var nameText = new TextBlock
        {
            Text = entry.Info.Nickname is { Length: > 0 } n ? n : SpeciesName.GetSpeciesName((ushort)entry.Info.Species, (int)LanguageID.English),
            FontSize = 10,
            Foreground = PkChrome.Brush(UiTokens.Ink1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0),
        };
        Control spriteControl = sprite is null
            ? new TextBlock { Text = "?", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            : new Image { Source = sprite, Stretch = Stretch.Uniform, Height = 84 };

        return new Grid
        {
            Children =
            {
                new StackPanel { Spacing = 0, Children = { spriteControl, nameText } },
            },
        };
    }

    private void RegisterCells()
    {
        for (var s = 0; s < 30; s++)
        {
            var cell = (s / 6, s % 6);
            var slot = s;
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
            border.PointerPressed += (_, e) => OnCellPressed(cell, slot, e);
            Canvas.SetLeft(border, (slot % Columns) * CellSize);
            Canvas.SetTop(border, (slot / Columns) * CellSize);
            _canvas.Children.Add(border);
            _cells[(cell.Item1, slot)] = border;
        }
    }

    private void OnCellPressed((int Box, int Slot) cell, int slot, PointerEventArgs e)
    {
        var entry = _vm.Entry(_vm.BoxIndex, slot);
        if (entry is null)
        {
            _selected = null;
            RefreshSummary();
            PaintSelection();
            return;
        }
        _selected = (_vm.BoxIndex, slot);
        RefreshSummary();
        PaintSelection();
        _dragFrom = (_vm.BoxIndex, slot);
    }

    private void OnDragMove(PointerEventArgs e)
    {
        if (_dragFrom is null) return;
        EnsureGhost(_dragFrom.Value);
        var pos = e.GetCurrentPoint(_canvas).Position;
        Canvas.SetLeft(_ghost!, pos.X - 40);
        Canvas.SetTop(_ghost!, pos.Y - 40);
    }

    private void EnsureGhost((int Box, int Slot) from)
    {
        if (_ghost is not null) return;
        var entry = _vm.Entry(from.Box, from.Slot);
        if (entry is null) return;
        var sprite = _sprites.GetSprite(entry.Info.Look);
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
        _canvas.Children.Add(_ghost);
    }

    private void OnDragRelease(PointerEventArgs e)
    {
        if (_dragFrom is null) return;
        RemoveGhost();
        var from = _dragFrom.Value;
        var pos = e.GetCurrentPoint(_canvas).Position;
        var col = (int)(pos.X / CellSize);
        var row = (int)(pos.Y / CellSize);
        _dragFrom = null;
        if (col < 0 || col >= Columns || row < 0 || row >= 5) return;
        var target = row * Columns + col;
        if (target == from.Slot) { RefreshGrid(); return; }
        var entry = _vm.Entry(from.Box, from.Slot);
        if (entry is null) { RefreshGrid(); return; }
        _vm.MoveBank(entry.Id, from.Box, target);
        RefreshGrid();
    }

    private void RemoveGhost()
    {
        if (_ghost is null) return;
        _canvas.Children.Remove(_ghost);
        _ghost = null;
    }

    private void CancelDrag()
    {
        RemoveGhost();
        _dragFrom = null;
    }

    private void PaintSelection()
    {
        foreach (var (cell, border) in _cells)
        {
            var selected = _selected is { } sel && sel == (cell.Box, cell.Slot);
            border.BorderBrush = selected ? PkChrome.Brush(UiTokens.Bright) : Brushes.Transparent;
            border.Background = selected ? PkChrome.Brush(UiTokens.RowStripe) : Brushes.Transparent;
        }
    }

    private Control SummarySlot()
    {
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(PkChrome.PixelLabel("SUMMARY", 16, UiTokens.Bright));
        stack.Children.Add(_summaryText);
        return stack;
    }

    private void RefreshSummary()
    {
        var entry = _selected is { } sel ? _vm.Entry(sel.Box, sel.Slot) : null;
        if (entry is null)
        {
            _summaryText.Text = "Click a Pokémon to see its details.";
            return;
        }
        var info = entry.Info;
        var species = SpeciesName.GetSpeciesName((ushort)info.Species, (int)LanguageID.English);
        var strings = GameInfo.GetStrings("en");
        var item = info.HeldItem is { } held && held > 0 ? strings.Item[held] : "—";
        _summaryText.Text = $"{species} · Lv. {info.Level} · Gen {info.Generation} · {info.Format}\nNickname: {info.Nickname}\nShiny: {(info.Shiny ? "YES" : "no")}\nHeld item: {item}\nFrom: {info.SourceName}";
    }

    private async ValueTask<string> ExportFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return "Cannot open the folder dialog here.";
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Export bank to a .pk folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return "Cancelled.";
        return _vm.ExportFolder(folders[0].Path.LocalPath);
    }

    private async ValueTask<string> ImportFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return "Cannot open the folder dialog here.";
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Import .pk files from a folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return "Cancelled.";
        var message = _vm.ImportFolder(folders[0].Path.LocalPath);
        RefreshGrid();
        return message;
    }
}
