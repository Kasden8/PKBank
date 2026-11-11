using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using PKBank.App.Navigation;
using PKBank.App.Services;
using PKBank.App.Services.Pokepark;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKForge.Engine;

namespace PKBank.App.Views;

/// <summary>
/// The PKBank home: a hero card with the identity, and the five doors into the app.
/// The Bank is the emotional center; everything else orbits it.
/// </summary>
public sealed class HomeView : UserControl
{
    public HomeView()
    {
        Background = Brushes.Transparent;
        var hero = Hero();
        var tiles = Tiles();
        Grid.SetRow(tiles, 1);
        var grid = new Grid
        {
            Margin = new Thickness(26, 22),
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition()],
            RowSpacing = 18,
            Children = { hero, tiles },
        };
        Content = grid;
    }

    private static Border Hero()
    {
        var icon = new Image
        {
            Source = new Avalonia.Media.Imaging.Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri("avares://PKBank/Assets/icon.png"))),
            Width = 92,
            Height = 92,
        };
        var tag = PkChrome.PixelLabel("POCKET STORAGE · PC", 11, UiTokens.Cyan);
        var title = PkChrome.PixelLabel("PKBank", 40, UiTokens.Bright);
        var sub = PkChrome.BodyLabel("Your whole collection, in one place — offline, on your PC, yours forever.", 13.5, UiTokens.Ink0, wrap: true);
        sub.MaxWidth = 520;
        var text = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { title, sub, tag } };

        var row = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Auto), new ColumnDefinition()], ColumnSpacing = 22 };
        row.Children.Add(icon);
        row.Children.Add(text);

        return new Border
        {
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(24, 22),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetX = 4, OffsetY = 4, Blur = 0, Color = Color.Parse("#70000000") }),
            Child = row,
        };
    }

    private static Control Tiles()
    {
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var (title, desc, tag) in TilesData)
            wrap.Children.Add(Tile(title, desc, tag));
        return wrap;
    }

    private static readonly (string Title, string Desc, string Tag)[] TilesData =
    [
        ("BANK", "The vault: send, copy, search and organize everything you own.", "bank"),
        ("SAVE EDITOR", "Open any save, edit boxes and party, live legality.", "saves"),
        ("LIVING DEX", "Track all 1025 species across every game, shiny included.", "dex"),
        ("EVENTS", "Mystery Gift cards, browsed offline, ready to hand out.", "events"),
        ("ABOUT", "Credits, licenses and the people behind PKBank.", "about"),
        ("POKÉPARK", "A living world: send Pokémon to roam, rest and socialize.", "park"),
    ];

    private static Border Tile(string title, string desc, string tag)
    {
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(PkChrome.PixelLabel(title, 15, UiTokens.Bright));
        var descText = PkChrome.BodyLabel(desc, 12, UiTokens.Ink1, wrap: true);
        descText.MaxHeight = 48;
        descText.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(descText);

        var border = new Border
        {
            Width = 268,
            Height = 150,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(16, 14),
            Background = PkChrome.ButtonGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.ButtonEdge),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(UiTokens.ControlRadius),
            Child = body,
        };
        border.PointerEntered += (_, _) =>
        {
            border.Background = PkChrome.Brush(UiTokens.SelectFill);
            border.BorderBrush = PkChrome.Brush(UiTokens.Rim);
        };
        border.PointerExited += (_, _) =>
        {
            border.Background = PkChrome.ButtonGradient();
            border.BorderBrush = PkChrome.Brush(UiTokens.ButtonEdge);
        };
        border.PointerPressed += (_, _) => Open(tag);
        return border;
    }

    private static void Open(string tag)
    {
        Control page = tag switch
        {
            "bank" => new PlaceholderView("BANK",
                "Send, copy, search and organize your whole collection — like Pokémon HOME, but yours, offline and on this PC.",
                "PHASE 4"),
            "saves" => new PlaceholderView("SAVE EDITOR",
                "Open any save, edit boxes and party, and watch legality live as you change things.",
                "PHASE 4"),
            "dex" => new PlaceholderView("LIVING DEX",
                "Track every species you own across your games and the Bank — normal and shiny, per generation, with forms.",
                "PHASE 4"),
            "events" => new PlaceholderView("EVENTS",
                "The full Mystery Gift database, browsed offline — wonder cards ready to hand out.",
                "PHASE 5"),
            "park" => new ParkView(
                App.Services.GetRequiredService<PokeparkService>(),
                App.Services.GetRequiredService<ParkSession>(),
                App.Services.GetRequiredService<ParkRenderer>(),
                App.Services.GetRequiredService<PokeparkSpriteService>(),
                App.Services.GetRequiredService<ParkDaycare>(),
                new BankViewModel(
                    App.Services.GetRequiredService<IBankService>(),
                    new SaveEngine()),
                null),
            _ => new AboutView(),
        };
        if (App.Services?.GetService<Navigator>() is { } navigator)
            navigator.Show(page);
    }
}
