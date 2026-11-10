using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PKForge.Domain;
using PKHeX.Core;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using SpeciesNameAlias = PKHeX.Core.SpeciesName;

namespace PKBank.App.Views;

/// <summary>
/// The Poképark (lite): the map, the current visitors, the care (feed/play), pair scenes, the
/// journal and send/release. The intricate PKForge social world (friendships/rivalries/narratives,
/// animated PMDCollab residents) arrives when it lands; this already behaves like a park.
/// </summary>
public sealed class ParkView : UserControl
{
    private readonly ParkStore _store;
    private readonly SpriteService _sprites;
    private readonly BankViewModel _bank;
    private readonly SaveEditorViewModel? _saveEditorVm;

    private readonly TextBlock _status = new() { FontSize = 12.5 };
    private readonly StackPanel _residentsPanel = new() { Spacing = 6 };
    private readonly StackPanel _journalPanel = new() { Spacing = 4 };

    private ParkPokemon? _selected;

    public ParkView(ParkStore store, SpriteService sprites, BankViewModel bank, SaveEditorViewModel? saveEditorVm)
    {
        _store = store;
        _sprites = sprites;
        _bank = bank;
        _saveEditorVm = saveEditorVm;
        Background = Brushes.Transparent;
        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);
        Content = BuildContent();
        Render();
    }

    private Control BuildContent()
    {
        var ground = LoadBitmap("Assets/park/ground.png");
        var objects = LoadBitmap("Assets/park/objects.png");
        Control? parkImage = null;
        if (ground is not null)
        {
            var layers = new Grid { Children = { new Image { Source = ground, Stretch = Stretch.UniformToFill, MinHeight = 240 } } };
            if (objects is not null)
                layers.Children.Add(new Image { Source = objects, Stretch = Stretch.UniformToFill, Opacity = 0.85 });
            parkImage = new Border
            {
                Child = layers,
                CornerRadius = new CornerRadius(12),
                BorderBrush = PkChrome.Brush(UiTokens.Rim),
                BorderThickness = new Thickness(1.2),
            };
        }
        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(PkChrome.PixelLabel("POKÉPARK", 24, UiTokens.Bright));
        header.Children.Add(PkChrome.BodyLabel("Up to 24 of your Pokémon live here, rest and roam. Care levels and the journal stick around after you leave.", 12.5, UiTokens.InkSoft));

        var columns = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(300))],
            ColumnSpacing = 16,
        };
        columns.Children.Add(_residentsPanel);
        var journalScroll = new ScrollViewer { Content = _journalPanel, MaxHeight = 240 };
        var side = new Border
        {
            Padding = new Thickness(14),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Child = new StackPanel { Spacing = 8, Children = { PkChrome.PixelLabel("PARK JOURNAL", 13, UiTokens.Bright), journalScroll } },
        };
        columns.Children.Add(side);
        Grid.SetColumn(side, 1);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var send = new Button { Content = "Send from open save" };
        SaveEditorView.StyleButton(send);
        send.Click += (_, _) => SendFromSaveEditor();
        var sendBank = new Button { Content = "Send first Bank entry" };
        SaveEditorView.StyleButton(sendBank);
        sendBank.Click += (_, _) => SendFromBank();
        var feed = new Button { Content = "Feed selected" };
        SaveEditorView.StyleButton(feed);
        feed.Click += (_, _) => { if (_selected is { } imm) { _store.Care(imm.Id, feed: true); Render(); } };
        var play = new Button { Content = "Play with selected" };
        SaveEditorView.StyleButton(play);
        play.Click += (_, _) => { if (_selected is { } imm) { _store.Care(imm.Id, feed: false); Render(); } };
        var sendAway = new Button { Content = "Send away selected" };
        SaveEditorView.StyleButton(sendAway);
        sendAway.Click += (_, _) => { if (_selected is { } imm) { _store.RemoveVisitor(imm.Id, $"{imm.Name} left the park."); Render(); } };
        var photo = new Button { Content = "Take photo" };
        SaveEditorView.StyleButton(photo);
        photo.Click += (_, _) => { if (_selected is { } imm) { _store.Care(imm.Id, feed: true); _status.Text = $"Photo album: {imm.Name} posed for the grainy 3DS camera."; } };
        buttons.Children.Add(send);
        buttons.Children.Add(sendBank);
        buttons.Children.Add(feed);
        buttons.Children.Add(play);
        buttons.Children.Add(photo);
        buttons.Children.Add(sendAway);

        var root = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(26, 22),
            Children = { header, parkImage ?? new Border(), columns, buttons, _status },
        };
        return root;
    }

    private void Render()
    {
        _residentsPanel.Children.Clear();
        var residents = _store.Roster;
        if (residents.Count == 0)
        {
            _residentsPanel.Children.Add(PkChrome.BodyLabel("No residents yet — send someone from a save or the Bank.", 13, UiTokens.InkSoft));
        }
        foreach (var resident in residents)
            _residentsPanel.Children.Add(ResidentRow(resident));

        _journalPanel.Children.Clear();
        foreach (var line in _store.Journal.TakeLast(30))
            _journalPanel.Children.Add(PkChrome.BodyLabel(line, 11.5, UiTokens.Ink1, wrap: true));
    }

    private Control ResidentRow(ParkPokemon resident)
    {
        var look = new SpriteLook(resident.Species, resident.Form, resident.Shiny);
        var sprite = _sprites.GetSprite(look);
        if (sprite is null) _sprites.Warm(look, () => Dispatcher.UIThread.Post(() => Render()));

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(sprite is null
            ? new TextBlock { Text = "○", FontSize = 28, VerticalAlignment = VerticalAlignment.Center, Foreground = PkChrome.Brush(UiTokens.InkSoft) }
            : new Image { Source = sprite, Width = 44, Height = 44, Stretch = Stretch.Uniform });
        left.Children.Add(new StackPanel
        {
            Spacing = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PkChrome.BodyLabel(resident.Name + " · Lv." + resident.Form, 13, UiTokens.Ink0),
                PkChrome.BodyLabel(resident.Source + " · " + _store.CareStatus(resident.Id), 11.5, UiTokens.InkSoft),
            },
        });

        var border = new Border
        {
            Padding = new Thickness(10, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Background = _selected is { } sel && sel.Id == resident.Id
                ? PkChrome.Brush(UiTokens.SelectFill)
                : PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(8),
            Child = left,
        };
        border.PointerPressed += (_, _) => { _selected = resident; Render(); };
        return border;
    }

    private void SendFromBank()
    {
        var first = _bank.Entries.FirstOrDefault();
        if (first is null)
        {
            _status.Text = "The Bank is empty.";
            return;
        }
        var info = first.Info;
        var visitor = new ParkPokemon(
            $"bank:{first.Id:N}",
            info.Species, info.Form, info.Shiny,
            info.Nickname is { Length: > 0 } n ? n : SpeciesNameAlias.GetSpeciesName((ushort)info.Species, (int)LanguageID.English),
            $"Bank — Box {first.Box + 1} — Slot {first.Slot + 1}",
            $"Gen {info.Generation}", "", info.SourceName ?? "");
        _store.AddVisitor(visitor, $"{visitor.Name} moved into the park!");
        _status.Text = $"{visitor.Name} is in the park now.";
        Render();
    }

    private void SendFromSaveEditor()
    {
        if (_saveEditorVm?.Save is null)
        {
            _status.Text = "Open a save in the editor first.";
            return;
        }
        var sav = _saveEditorVm.Save;
        var candidates = new List<(int box, int slot, PKM Entity)>();
        for (var b = 0; b < sav.BoxCount; b++)
            for (var s = 0; s < sav.BoxSlotCount; s++)
            {
                var mon = sav.GetBoxSlotAtIndex(b, s);
                if (mon.Species > 0 && !mon.IsEgg) candidates.Add((b, s, mon));
            }
        var chosen = candidates.FirstOrDefault();
        if (chosen.Entity is null)
        {
            _status.Text = "No visitors available in this save.";
            return;
        }
        var pk = chosen.Entity;
        var visitor = new ParkPokemon(
            $"save:{Math.Abs((int)pk.GetHashCode())}:{chosen.box}:{chosen.slot}",
            pk.Species, pk.Form, pk.IsShiny,
            string.IsNullOrWhiteSpace(pk.Nickname) ? SpeciesNameAlias.GetSpeciesName(pk.Species, (int)LanguageID.English) : pk.Nickname,
            $"Save — Box {chosen.box + 1} — Slot {chosen.slot + 1}",
            $"Gen {sav.Generation}", sav.OT, _saveEditorVm.FileName);
        _store.AddVisitor(visitor, $"{visitor.Name} moved into the park!");
        _status.Text = $"{visitor.Name} is in the park now.";
        Render();
    }

    private static Bitmap? LoadBitmap(string uri)
    {
        try { return new Bitmap(AssetLoader.Open(new Uri("avares://PKBank/" + uri))); }
        catch (Exception) { return null; }
    }
}
