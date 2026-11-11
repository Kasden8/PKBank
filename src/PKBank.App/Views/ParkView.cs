using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using PKBank.App.Services;
using PKBank.App.Services.Pokepark;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKHeX.Core;
using SkiaSharp;
using SpeciesNameAlias = PKHeX.Core.SpeciesName;

namespace PKBank.App.Views;

/// <summary>
/// The Poképark: the living world (the simulation, the residents, the journal and the goals)
/// with the park's map in the middle. The residents' real data stays in the games; the park
/// only remembers their faces.
/// </summary>
public sealed class ParkView : UserControl
{
    private readonly PokeparkService _service;
    private readonly ParkSession _session;
    private readonly ParkRenderer _renderer;
    private readonly ParkDaycare _daycare;
    private readonly ParkCanvas _canvas;
    private readonly BankViewModel _bank;
    private readonly SaveEditorViewModel? _saveEditorVm;
    private readonly PokeparkSpriteService _sprites;

    private readonly TextBlock _status = new() { FontSize = 12.5 };
    private readonly StackPanel _residentsPanel = new() { Spacing = 4 };
    private readonly StackPanel _journalPanel = new() { Spacing = 3 };
    private readonly StackPanel _goalsPanel = new() { Spacing = 3 };
    private readonly StackPanel _daycarePanel = new() { Spacing = 3 };
    private readonly StackPanel _cardPanel = new() { Spacing = 4 };
    private readonly Border _cardHost = new() { IsVisible = false };
    private readonly Border _albumHost = new() { IsVisible = false };
    private readonly Border _creditsHost = new() { IsVisible = false };
    private readonly ScrollViewer _journalScroll;

    private string? _selected;
    private bool _refreshing;

    public ParkView(PokeparkService service, ParkSession session, ParkRenderer renderer, PokeparkSpriteService sprites,
        ParkDaycare daycare, BankViewModel bank, SaveEditorViewModel? saveEditorVm)
    {
        _service = service;
        _session = session;
        _renderer = renderer;
        _sprites = sprites;
        _daycare = daycare;
        _bank = bank;
        _saveEditorVm = saveEditorVm;
        _service.CurrentSave = () => _saveEditorVm?.Save;
        _daycare.SaveEditor = () => _saveEditorVm;

        _canvas = new ParkCanvas(renderer, session);
        _canvas.SelectionChanged += id =>
        {
            _selected = id;
            Render();
        };
        _canvas.MapClicked += OnMapClicked;
        _canvas.MapDragged += OnMapDragged;

        _journalScroll = new ScrollViewer { Content = _journalPanel, MaxHeight = 190 };
        Background = Brushes.Transparent;
        Content = BuildContent();
        Load();
    }

    /// <summary>Fills the park (the first visit) and restores the residents' care.</summary>
    private void Load()
    {
        _service.AutoFillIfEmpty();
        var roster = _service.LoadRoster();
        _session.Load(roster);
        _renderer.Residents = roster;
        _renderer.Life = _session.Life;
        _session.RunOfflineLife();
        _selected = roster.FirstOrDefault()?.Id;
        _renderer.SelectedId = _selected;
        Render();
    }

    private Control BuildContent()
    {
        var header = new StackPanel { Spacing = 3 };
        header.Children.Add(PkChrome.PixelLabel("POKÉPARK", 24, UiTokens.Bright));
        header.Children.Add(PkChrome.BodyLabel(
            "Your Pokémon live here between visits: they wander, meet, quarrel, make friends and need care. Their real data stays in the save or the Bank.",
            12.5, UiTokens.InkSoft, wrap: true));

        var world = new Grid { RowDefinitions = [new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto)] };
        var canvasBorder = new Border
        {
            Child = _canvas,
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#233a39")),
        };
        world.Children.Add(canvasBorder);
        var overlay = new Grid();
        overlay.Children.Add(_cardHost);
        overlay.Children.Add(_albumHost);
        overlay.Children.Add(_creditsHost);
        world.Children.Add(overlay);

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        AddButton(toolbar, "Talk", () => Interact(ParkCommand.Talk));
        AddButton(toolbar, "Snack", () => Interact(ParkCommand.Snack));
        AddButton(toolbar, "Play", () => Interact(ParkCommand.Play));
        AddButton(toolbar, "Relax", () => Interact(ParkCommand.Relax));
        AddButton(toolbar, "Pat", () => Pat());
        AddButton(toolbar, "Send from save", () => SendFromSave());
        AddButton(toolbar, "Send from Bank", () => SendFromBank());
        AddButton(toolbar, "Photo", () => TakePhoto());
        AddButton(toolbar, "Album", () => ShowAlbum());
        AddButton(toolbar, "Credits", () => ShowCredits());
        Grid.SetRow(toolbar, 1);
        world.Children.Add(toolbar);

        var columns = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(new GridLength(320))],
            ColumnSpacing = 14,
        };
        columns.Children.Add(world);
        var side = BuildSidePanel();
        columns.Children.Add(side);
        Grid.SetColumn(side, 1);

        var root = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24, 20),
            Children = { header, columns, _status },
        };
        return root;
    }

    private Control BuildSidePanel()
    {
        var panel = new StackPanel { Spacing = 10 };

        panel.Children.Add(PkChrome.PixelLabel("RESIDENTS", 13, UiTokens.Bright));
        var residentsScroll = new ScrollViewer { Content = _residentsPanel, MaxHeight = 250 };
        panel.Children.Add(residentsScroll);

        panel.Children.Add(PkChrome.PixelLabel("TODAY'S GOALS", 13, UiTokens.Bright));
        panel.Children.Add(_goalsPanel);

        panel.Children.Add(PkChrome.PixelLabel("DAYCARE", 13, UiTokens.Bright));
        panel.Children.Add(_daycarePanel);

        panel.Children.Add(PkChrome.PixelLabel("JOURNAL", 13, UiTokens.Bright));
        panel.Children.Add(_journalScroll);

        return new Border
        {
            Padding = new Thickness(12),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Child = panel,
        };
    }

    private static void AddButton(Panel parent, string text, Action action)
    {
        var button = new Button { Content = text };
        SaveEditorView.StyleButton(button);
        button.Click += (_, _) => action();
        parent.Children.Add(button);
    }

    private void Render()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            RenderResidents();
            RenderGoals();
            RenderDaycare();
            RenderJournal();
            RenderCard();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RenderResidents()
    {
        _residentsPanel.Children.Clear();
        var residents = _service.Roster;
        if (residents.Count == 0)
        {
            _residentsPanel.Children.Add(PkChrome.BodyLabel("No residents yet — send someone from a save or the Bank.", 12.5, UiTokens.InkSoft, wrap: true));
            return;
        }
        foreach (var resident in residents)
            _residentsPanel.Children.Add(ResidentRow(resident));
    }

    private Control ResidentRow(ParkPokemon resident)
    {
        var portrait = PortraitOf(resident);
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(portrait is null
            ? new TextBlock { Text = "○", FontSize = 24, VerticalAlignment = VerticalAlignment.Center, Foreground = PkChrome.Brush(UiTokens.InkSoft) }
            : new Image { Source = portrait, Width = 34, Height = 34, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        var mood = MoodOf(resident);
        left.Children.Add(new StackPanel
        {
            Spacing = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                PkChrome.BodyLabel(resident.Name + (resident.Shiny ? " ✦" : ""), 12.5, UiTokens.Ink0),
                PkChrome.BodyLabel($"{mood} · {resident.Source}", 11, UiTokens.InkSoft, wrap: true),
            },
        });

        var border = new Border
        {
            Padding = new Thickness(8, 5),
            Background = _selected == resident.Id ? PkChrome.Brush(UiTokens.SelectFill) : PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(_selected == resident.Id ? UiTokens.SelectBorder : UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(6),
            Child = left,
        };
        border.PointerPressed += (_, _) =>
        {
            _selected = resident.Id;
            _renderer.SelectedId = resident.Id;
            Render();
        };
        return border;
    }

    private void RenderGoals()
    {
        _goalsPanel.Children.Clear();
        var goals = _session.Goals;
        if (goals.Goals.Count == 0)
        {
            _goalsPanel.Children.Add(PkChrome.BodyLabel("No goals yet — the park needs residents.", 12, UiTokens.InkSoft, wrap: true));
            return;
        }
        for (var i = 0; i < goals.Goals.Count; i++)
        {
            var goal = goals.Goals[i];
            var done = goals.Done(i);
            _goalsPanel.Children.Add(PkChrome.BodyLabel(
                done ? "✓ " : "• " + goal.Text + $" ({goals.Progress[i]}/{goal.Count})",
                12, done ? UiTokens.Ok : UiTokens.Ink1, wrap: true));
        }
    }

    /// <summary>The residents at the daycare, and how much they have grown.</summary>
    private void RenderDaycare()
    {
        _daycarePanel.Children.Clear();
        var guests = _daycare.Guests;
        if (guests.Count == 0)
        {
            _daycarePanel.Children.Add(PkChrome.BodyLabel("Nobody is staying at the daycare.", 12, UiTokens.InkSoft, wrap: true));
            return;
        }
        foreach (var guest in guests)
        {
            var resident = _service.Roster.FirstOrDefault(r => r.Id == guest.Id);
            var name = resident?.Name ?? guest.Id;
            var preview = resident is null ? null : _daycare.Preview(resident);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            row.Children.Add(PkChrome.BodyLabel(
                preview is { } p ? $"{name} · Lv.{p.Before}→{p.After}" : name, 11.5, UiTokens.Ink1));
            var collect = new Button { Content = "Collect" };
            SaveEditorView.StyleButton(collect);
            collect.FontSize = 11;
            collect.Padding = new Thickness(8, 3);
            collect.Click += (_, _) => _ = CollectAsync(resident);
            row.Children.Add(collect);
            _daycarePanel.Children.Add(row);
        }
    }

    /// <summary>Takes a resident home from the daycare, writing the growth back.</summary>
    private async Task CollectAsync(ParkPokemon? resident)
    {
        if (resident is null) return;
        _status.Text = await _daycare.CollectAsync(resident).ConfigureAwait(true);
        Render();
    }

    private void RenderJournal()
    {
        _journalPanel.Children.Clear();
        var journal = _service.Journal;
        if (journal.Count == 0)
        {
            _journalPanel.Children.Add(PkChrome.BodyLabel("Nothing has happened yet.", 12, UiTokens.InkSoft));
            return;
        }
        foreach (var entry in journal.TakeLast(30).Reverse())
            _journalPanel.Children.Add(PkChrome.BodyLabel($"{entry.Timestamp.ToLocalTime():HH:mm}  {entry.Text}", 11.5, UiTokens.Ink1, wrap: true));
    }

    private void RenderCard()
    {
        _cardPanel.Children.Clear();
        var resident = _service.Roster.FirstOrDefault(r => r.Id == _selected);
        if (resident is null)
        {
            _cardHost.IsVisible = false;
            return;
        }
        var care = _session.Life[resident.Id]?.Care;
        var portrait = PortraitOf(resident);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        if (portrait is not null) header.Children.Add(new Image { Source = portrait, Width = 48, Height = 48, Stretch = Stretch.Uniform });
        header.Children.Add(new StackPanel
        {
            Spacing = 1,
            Children =
            {
                PkChrome.PixelLabel(resident.Name.ToUpperInvariant(), 16, UiTokens.Bright),
                PkChrome.BodyLabel($"{SpeciesName(resident.Species)} · {MoodOf(resident)}", 12, UiTokens.Ink1),
                PkChrome.BodyLabel(resident.Source, 11.5, UiTokens.InkSoft, wrap: true),
            },
        });
        _cardPanel.Children.Add(header);
        if (care is not null)
            _cardPanel.Children.Add(PkChrome.BodyLabel(
                $"Hunger {care.Hunger:0}  ·  Energy {care.Energy:0}  ·  Fun {care.Fun:0}  ·  Social {care.Social:0}  ·  Bond {care.Bond:0}",
                11.5, UiTokens.InkSoft, wrap: true));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
        AddButton(actions, "Talk", () => Interact(ParkCommand.Talk));
        AddButton(actions, "Snack", () => Interact(ParkCommand.Snack));
        AddButton(actions, "Play", () => Interact(ParkCommand.Play));
        AddButton(actions, "Relax", () => Interact(ParkCommand.Relax));
        AddButton(actions, "Send home", () => SendHome(resident));
        _cardPanel.Children.Add(actions);

        var games = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        AddButton(games, "Hide & seek", () => StartGame(ParkGameKind.HideAndSeek));
        AddButton(games, "Race", () => StartGame(ParkGameKind.Race));
        AddButton(games, "Brush", () => StartGame(ParkGameKind.Brush));
        AddButton(games, "Spar", () => StartGame(ParkGameKind.Spar));
        AddButton(games, "Stop", () => { _session.Life.EndGame(); _status.Text = "The game is over."; Render(); });
        _cardPanel.Children.Add(games);

        var daycare = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        var atDaycare = _daycare.Guests.Any(g => g.Id == resident.Id);
        AddButton(daycare, atDaycare ? "Take home" : "Leave at daycare", () => ToggleDaycare(resident, atDaycare));
        var other = _service.Roster.FirstOrDefault(r => r.Id != resident.Id);
        if (other is not null)
            AddButton(daycare, "Breed with " + other.Name, () => Breed(resident, other));
        _cardPanel.Children.Add(daycare);

        var bonds = _session.Relationships
            .Where(r => r.FirstId == resident.Id || r.SecondId == resident.Id)
            .Select(r => (Relationship: r, Other: NameOf(r.FirstId == resident.Id ? r.SecondId : r.FirstId)))
            .OrderByDescending(b => b.Relationship.Friendship + b.Relationship.Affection)
            .Take(4)
            .ToArray();
        if (bonds.Length > 0)
        {
            _cardPanel.Children.Add(PkChrome.PixelLabel("BONDS", 11, UiTokens.Bright));
            foreach (var bond in bonds)
                _cardPanel.Children.Add(PkChrome.BodyLabel(
                    $"{bond.Other}: {BondWord(bond.Relationship)}", 11.5, UiTokens.InkSoft, wrap: true));
        }

        _cardHost.Child = new Border
        {
            Padding = new Thickness(14),
            Margin = new Thickness(14),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 460,
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(UiTokens.PanelRadius),
            Child = _cardPanel,
        };
        _cardHost.IsVisible = true;
    }

    /// <summary>How two residents get on, in words.</summary>
    private static string BondWord(ParkRelationship relationship)
    {
        if (relationship.Rivalry > 60) return "rivals";
        if (relationship.Friendship > 70) return "best friends";
        if (relationship.Friendship > 40) return "friends";
        if (relationship.Friendship > 15) return "acquainted";
        if (relationship.Rivalry > 20) return "on bad terms";
        return "strangers";
    }

    private string MoodOf(ParkPokemon resident)
    {
        var actor = _session.Life[resident.Id];
        return actor?.Mood ?? "Settling in";
    }

    private string SpeciesName(int species) => _service.SpeciesName(species);

    /// <summary>The resident's portrait: the PMD one when it has loaded, the bundled sprite otherwise.</summary>
    private Bitmap? PortraitOf(ParkPokemon resident)
    {
        var portrait = _sprites.GetPortrait(resident.Species, resident.Form, resident.Shiny);
        if (portrait is not null) return ToBitmap(portrait);
        return null;
    }

    private static Bitmap? ToBitmap(SKBitmap bitmap)
    {
        try
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return new Bitmap(new MemoryStream(data.ToArray()));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Interact(ParkCommand command)
    {
        if (_selected is null) { _status.Text = "Pick a resident first."; return; }
        var reply = _session.Interact(_selected, command);
        _status.Text = reply.Accepted
            ? reply.Line ?? $"{command} — {NameOf(_selected)} likes it."
            : $"{NameOf(_selected)} is not in the mood right now.";
        Render();
    }

    /// <summary>Starts a game with the selected resident.</summary>
    private void StartGame(ParkGameKind kind)
    {
        if (_selected is null) { _status.Text = "Pick a resident first."; return; }
        var reply = _session.Life.StartGame(kind, _selected);
        _status.Text = reply.Accepted
            ? kind switch
            {
                ParkGameKind.HideAndSeek => "Eyes closed… click where the resident hides!",
                ParkGameKind.Race => "The race starts! Click your resident to cheer it on.",
                ParkGameKind.Brush => "Drag the mouse over the resident to brush it.",
                ParkGameKind.Spar => "A training session! Click when it strikes to block.",
                _ => "The game begins!",
            }
            : $"{NameOf(_selected)} is not in the mood for that game.";
        Render();
    }

    /// <summary>Leaves a resident at the daycare, or brings it back.</summary>
    private void ToggleDaycare(ParkPokemon resident, bool atDaycare)
    {
        _status.Text = atDaycare ? _daycare.TakeHome(resident) : _daycare.Leave(resident);
        Render();
    }

    /// <summary>Leaves an egg at the daycare for two residents.</summary>
    private void Breed(ParkPokemon first, ParkPokemon second)
    {
        _status.Text = _daycare.Breed(first, second);
        Render();
    }

    /// <summary>What a click on the world means: the game the player is playing.</summary>
    private void OnMapClicked(ParkPoint point)
    {
        var game = _session.Life.Game;
        if (game is not { Stage: not ParkGameStage.Over }) return;
        switch (game.Kind)
        {
            case ParkGameKind.HideAndSeek:
                var found = _session.Life.Seek(point);
                _status.Text = found ? "You found it!" : "Nothing there…";
                break;
            case ParkGameKind.Race:
                if (_selected is not null) _session.Life.Cheer(_selected);
                _status.Text = "You cheer your resident on.";
                break;
            case ParkGameKind.Spar:
                _session.Life.Block();
                _status.Text = "You block!";
                break;
        }
        Render();
    }

    /// <summary>Dragging over the world brushes the resident in a brushing game.</summary>
    private void OnMapDragged(ParkPoint point)
    {
        var game = _session.Life.Game;
        if (game is not { Kind: ParkGameKind.Brush, Stage: not ParkGameStage.Over }) return;
        _session.Life.Brush(4f);
        if (game.Progress >= 1)
        {
            _status.Text = "Its coat shines!";
            _session.Record(ParkDeed.Brushed, game.HostId);
        }
    }

    private void Pat()
    {
        if (_selected is null) { _status.Text = "Pick a resident first."; return; }
        var reply = _session.Pet(_selected);
        _status.Text = reply.Accepted ? reply.Line ?? $"{NameOf(_selected)} enjoys the pat." : $"{NameOf(_selected)} dodged your hand.";
        Render();
    }

    private void SendHome(ParkPokemon resident)
    {
        _service.RemoveVisitor(resident.Id);
        _selected = null;
        _renderer.SelectedId = null;
        var roster = _service.LoadRoster();
        _session.Load(roster);
        _renderer.Residents = roster;
        _status.Text = $"{resident.Name} went home.";
        Render();
    }

    private void SendFromSave()
    {
        if (_saveEditorVm?.Save is null) { _status.Text = "Open a save in the editor first."; return; }
        var sav = _saveEditorVm.Save;
        var candidates = new List<(int Box, int Slot, PKM Entity)>();
        for (var b = 0; b < sav.BoxCount; b++)
        for (var s = 0; s < sav.BoxSlotCount; s++)
        {
            var mon = sav.GetBoxSlotAtIndex(b, s);
            if (mon.Species > 0 && !mon.IsEgg) candidates.Add((b, s, mon));
        }
        if (candidates.Count == 0) { _status.Text = "No visitors available in this save."; return; }
        var chosen = candidates[0];
        var message = _service.AddSaveVisitor(chosen.Box, chosen.Slot);
        _status.Text = message;
        if (message.StartsWith("now lives", StringComparison.Ordinal))
        {
            var roster = _service.LoadRoster();
            _session.Load(roster);
            _renderer.Residents = roster;
        }
        Render();
    }

    private void SendFromBank()
    {
        var first = _bank.CurrentEntries.FirstOrDefault();
        if (first is null) { _status.Text = "The Bank is empty."; return; }
        var message = _service.AddBankVisitor(first.Id);
        _status.Text = message;
        if (message.StartsWith("now lives", StringComparison.Ordinal))
        {
            var roster = _service.LoadRoster();
            _session.Load(roster);
            _renderer.Residents = roster;
        }
        Render();
    }

    /// <summary>Takes a photo of the park: the frame as it stands, saved in the album.</summary>
    private void TakePhoto()
    {
        try
        {
            using var bitmap = new SKBitmap(new SKImageInfo(640, 360, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            var renderer = _renderer;
            renderer.Draw(canvas, 640, 360, Environment.TickCount64);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
            var path = PokeparkStore.SavePhoto(data.ToArray());
            _status.Text = "Photo saved: " + Path.GetFileName(path);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException)
        {
            _status.Text = "The camera would not cooperate.";
        }
    }

    private void ShowAlbum()
    {
        var photos = PokeparkStore.Photos();
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(PkChrome.PixelLabel("PHOTO ALBUM", 15, UiTokens.Bright));
        if (photos.Count == 0)
            panel.Children.Add(PkChrome.BodyLabel("No photos yet — take one with the camera button.", 12, UiTokens.InkSoft, wrap: true));
        var grid = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 520 };
        foreach (var path in photos.Take(24))
        {
            try
            {
                grid.Children.Add(new Image
                {
                    Source = new Bitmap(path),
                    Width = 160,
                    Height = 90,
                    Stretch = Stretch.UniformToFill,
                    Margin = new Thickness(2),
                });
            }
            catch (Exception error) when (error is IOException or ArgumentException) { }
        }
        panel.Children.Add(new ScrollViewer { Content = grid, MaxHeight = 380 });
        var close = new Button { Content = "Close" };
        SaveEditorView.StyleButton(close);
        close.Click += (_, _) => _albumHost.IsVisible = false;
        panel.Children.Add(close);
        _albumHost.Child = OverlayFrame(panel);
        _albumHost.IsVisible = true;
    }

    private async void ShowCredits()
    {
        var panel = new StackPanel { Spacing = 6, Width = 520 };
        panel.Children.Add(PkChrome.PixelLabel("CREDITS", 15, UiTokens.Bright));
        panel.Children.Add(PkChrome.BodyLabel(
            "The park's map is by spritedmistery (tileset by AlchemyBats). The residents' sprites and portraits are from PMDCollab's SpriteCollab, fetched at runtime (CC BY-NC 4.0).",
            12, UiTokens.Ink1, wrap: true));

        var names = await _sprites.CreditNamesAsync().ConfigureAwait(true);
        var artists = new HashSet<string>(StringComparer.Ordinal);
        foreach (var resident in _service.Roster)
        {
            var credits = _sprites.GetAttribution(resident.Species, resident.Form, resident.Shiny);
            foreach (var artist in PokeparkSpriteService.Artists(credits, names))
                artists.Add(artist);
        }
        if (artists.Count > 0)
        {
            panel.Children.Add(PkChrome.PixelLabel("SPRITE ARTISTS", 12, UiTokens.Bright));
            panel.Children.Add(PkChrome.BodyLabel(string.Join(", ", artists), 11.5, UiTokens.InkSoft, wrap: true));
        }
        else
        {
            panel.Children.Add(PkChrome.BodyLabel(
                "The sprite artists' names appear once the residents' sprites have been downloaded (they need a network connection the first time).",
                11.5, UiTokens.InkSoft, wrap: true));
        }
        var close = new Button { Content = "Close" };
        SaveEditorView.StyleButton(close);
        close.Click += (_, _) => _creditsHost.IsVisible = false;
        panel.Children.Add(close);
        _creditsHost.Child = OverlayFrame(panel);
        _creditsHost.IsVisible = true;
    }

    private static Border OverlayFrame(Control content) => new()
    {
        Padding = new Thickness(16),
        Margin = new Thickness(18),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Background = PkChrome.PanelGradient(),
        BorderBrush = PkChrome.Brush(UiTokens.Rim),
        BorderThickness = new Thickness(1.4),
        CornerRadius = new CornerRadius(UiTokens.PanelRadius),
        Child = content,
    };

    private string NameOf(string id) => _service.Roster.FirstOrDefault(r => r.Id == id)?.Name ?? id;

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _session.SaveCare();
        base.OnDetachedFromVisualTree(e);
    }
}
