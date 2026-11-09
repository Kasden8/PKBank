using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>The bag editor: one row per pouch slot with the item name and a count field.</summary>
public sealed class BagEditorView : UserControl
{
    private readonly SaveEditorViewModel _vm;
    private readonly TextBlock _status = new();
    private readonly List<(InventoryPouch Pouch, NumericUpDown Count)> _fields = [];

    public BagEditorView(SaveEditorViewModel vm)
    {
        _vm = vm;
        Background = Brushes.Transparent;
        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        var root = new StackPanel { Spacing = 14, Margin = new Thickness(26, 22) };
        root.Children.Add(PkChrome.PixelLabel("BAG", 26, UiTokens.Bright));
        root.Children.Add(PkChrome.BodyLabel("Quantities per pouch; Apply copies them into the save (and marks it dirty).", 12.5, UiTokens.InkSoft));

        var save = _vm.Save;
        if (save is null)
        {
            root.Children.Add(PkChrome.BodyLabel("Open a save in the editor first.", 13.5, UiTokens.Ink0));
            return root;
        }

        var bag = save.Inventory;
        if (bag.Pouches.Count == 0)
        {
            root.Children.Add(PkChrome.BodyLabel("This save has no item pouch we can edit (unsupported format).", 13.5, UiTokens.InkSoft));
            return root;
        }

        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);
        _status.FontSize = 12.5;

        var panelContent = new StackPanel { Spacing = 12 };
        foreach (var pouch in bag.Pouches)
        {
            var sectionTitle = PkChrome.PixelLabel(pouch.Type.ToString(), 12, UiTokens.Cyan);
            panelContent.Children.Add(sectionTitle);
            var names = PouchNames(bag, pouch);
            var items = pouch.Items;
            var pouchRows = new StackPanel { Spacing = 4, Margin = new Thickness(4, 0, 4, 0) };
            for (var i = 0; i < items.Length; i++)
            {
                var index = items[i].Index;
                var name = index > 0 && (uint)index < (uint)names.Length && names[index].Length > 0 ? names[index] : $"#{index}";
                var label = PkChrome.BodyLabel($"{i + 1,2} · {name}", 12, UiTokens.Ink0);
                var count = new NumericUpDown
                {
                    Value = items[i].Count,
                    Minimum = 0,
                    Maximum = 9999,
                    ClipValueToMinMax = true,
                    Width = 64,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
                _fields.Add((pouch, count));
                var row = new Grid
                {
                    ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                    Children = { label, count },
                };
                Grid.SetColumn(count, 1);
                pouchRows.Children.Add(row);
            }
            panelContent.Children.Add(pouchRows);
        }

        var apply = new Button { Content = "Apply bag to save" };
        SaveEditorView.StyleButton(apply);
        apply.Click += (_, _) => Apply(bag);
        panelContent.Children.Add(apply);
        panelContent.Children.Add(_status);

        var scroll = new ScrollViewer { Content = panelContent };
        root.Children.Add(scroll);
        return root;
    }

    private void Apply(PlayerBag bag)
    {
        try
        {
            var byPouch = _fields
                .GroupBy(f => f.Pouch)
                .Select(g => (g.Key, Counts: g.Select(f => (int)f.Count.Value!).ToArray()))
                .ToList();
            foreach (var (pouch, counts) in byPouch)
            {
                var items = pouch.Items;
                for (var i = 0; i < Math.Min(items.Length, counts.Length); i++)
                    items[i].Count = Math.Clamp(counts[i], 0, items[i].Index == -1 ? 0 : pouch.MaxCount);
            }
            bag.CopyTo(_vm.Save!);
            _vm.Dirty = true;
            _status.Text = "Applied — Save in the editor to persist.";
        }
        catch (Exception error)
        {
            _status.Text = "Could not apply the bag: " + error.Message;
        }
    }

    private string[] PouchNames(PlayerBag bag, InventoryPouch pouch)
    {
        var strings = GameInfo.GetStrings("en");
        var names = new string[65536];
        foreach (var id in bag.Info.GetItems(pouch.Type))
        {
            if ((uint)id < (uint)strings.Item.Count)
                names[id] = strings.Item[(int)id];
        }
        return names;
    }
}
