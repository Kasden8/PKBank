using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using PKBank.App.Navigation;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKBank.App.Views;
using PKForge.Domain;
using PKForge.Engine;
using PKForge.Infrastructure;

namespace PKBank.App.Views;

public partial class MainWindow : Window
{
    private readonly Navigator _navigator;
    private readonly Dictionary<string, Border> _rail = new(StringComparer.Ordinal);
    private string _currentRail = "home";
    private SaveEditorViewModel? _currentSaveEditorVm;
    private BankViewModel? _currentBankVm;

    public MainWindow()
    {
        InitializeComponent();
        AppLog.Install();
        AppLog.Info("app", "MainWindow constructed");

        _navigator = App.Services.GetRequiredService<Navigator>();
        Host.Content = _navigator.Current;
        _navigator.ViewChanged += OnViewChanged;

        // Chrome: shared logo-grid backdrop baked once (Skia) and reused for the session.
        using var baked = BackdropBake.BakeLogoGrid(1408, 720);
        Background = new ImageBrush
        {
            Source = new Bitmap(baked),
            Stretch = Stretch.Fill,
        };

        StyleHeader();
        BuildRail();
        BuildFooter();
        Navigate("home");
    }

    private void StyleHeader()
    {
        Header.Background = PkChrome.StripGradient();
        Header.BorderBrush = PkChrome.Brush(UiTokens.Rim);
        Header.BorderThickness = new Thickness(0, 0, 0, 1.2);
        HeaderTitle.FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12");
        HeaderTitle.FontSize = 22;
        HeaderTitle.Foreground = PkChrome.Brush(UiTokens.Bright);
        HeaderTag.FontSize = 11.5;
        HeaderTag.Foreground = PkChrome.Brush(UiTokens.InkSoft);
        HeaderTag.Margin = new Thickness(0, 3, 0, 0);
        HeaderRight.FontFamily = new FontFamily("avares://PKBank/Assets/Fonts/NDS12.ttf#NDS12");
        HeaderRight.FontSize = 11;
        HeaderRight.Foreground = PkChrome.Brush(UiTokens.InkSoft);
    }

    private Border RailButton(string label, string tag)
    {
        var text = PkChrome.PixelLabel(label, 13, UiTokens.InkSoft);
        var border = new Border
        {
            Child = text,
            Padding = new Thickness(14, 9),
            CornerRadius = new CornerRadius(UiTokens.ControlRadius),
            Margin = new Thickness(0, 2),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1.2),
        };
        border.PointerPressed += (_, _) => Navigate(tag);
        border.PointerEntered += (_, _) =>
        {
            if (_currentRail != tag) border.Background = PkChrome.Brush(UiTokens.RowStripe);
        };
        border.PointerExited += (_, _) =>
        {
            if (_currentRail != tag) border.Background = Brushes.Transparent;
        };
        return border;
    }

    private void BuildRail()
    {
        Rail.Background = PkChrome.Brush(UiTokens.MaroonDeep);
        Rail.BorderBrush = PkChrome.Brush(UiTokens.Divider);
        Rail.BorderThickness = new Thickness(0, 0, 1, 0);
        var stack = new StackPanel { Margin = new Thickness(12, 14) };
        var caption = PkChrome.PixelLabel("MENU", 10, UiTokens.InkSoft);
        caption.Margin = new Thickness(14, 0, 0, 6);
        stack.Children.Add(caption);
        foreach (var (label, tag) in new (string Label, string Tag)[]
        {
            ("HOME", "home"),
            ("BANK", "bank"),
            ("SAVE EDITOR", "saves"),
            ("LIVING DEX", "dex"),
            ("POKÉPARK", "park"),
            ("EVENTS", "events"),
            ("ABOUT", "about"),
        })
        {
            var button = RailButton(label, tag);
            _rail[tag] = button;
            stack.Children.Add(button);
        }
        Rail.Child = new ScrollViewer { Content = stack };
    }

    private void BuildFooter()
    {
        Footer.Background = PkChrome.Brush(UiTokens.Well);
        Footer.BorderBrush = PkChrome.Brush(UiTokens.Divider);
        Footer.BorderThickness = new Thickness(0, 1, 0, 0);
        FooterCredit.FontSize = 11;
        FooterCredit.Foreground = PkChrome.Brush(UiTokens.InkSoft);

        AddHint("a", "Select");
        AddHint("b", "Back");
    }

    private void AddHint(string key, string label)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(new Image
        {
            Source = new Bitmap(Avalonia.Platform.AssetLoader.Open(new Uri($"avares://PKBank/Assets/key_{key}.png"))),
            Width = 18,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(PkChrome.PixelLabel(label, 11, UiTokens.Ink0));
        FooterHints.Children.Add(row);
    }

    private void OnViewChanged(Control view) => Host.Content = view;

    private void Navigate(string tag)
    {
        _currentRail = tag;
        foreach (var (key, button) in _rail)
        {
            var selected = key == tag;
            button.Background = selected ? PkChrome.Brush(UiTokens.MenuBlue) : Brushes.Transparent;
            button.BorderBrush = selected ? PkChrome.Brush(UiTokens.Rim) : Brushes.Transparent;
            if (button.Child is TextBlock text)
                text.Foreground = PkChrome.Brush(selected ? UiTokens.Bright : UiTokens.InkSoft);
        }

        switch (tag)
        {
            case "home":
                _navigator.ShowHome();
                break;
            case "saves":
                _currentSaveEditorVm = new SaveEditorViewModel(
                    App.Services.GetRequiredService<IBackupService>(),
                    App.Services.GetRequiredService<ISafeSaveWriter>(),
                    App.Services.GetRequiredService<IBankService>(),
                    App.Services.GetRequiredService<SaveEngine>());
                _navigator.Show(new SavesView(
                    _currentSaveEditorVm,
                    App.Services.GetRequiredService<SpriteService>()));
                break;
            case "bank":
                var bankVm = new BankViewModel(
                    App.Services.GetRequiredService<IBankService>(),
                    new SaveEngine());
                bankVm.CurrentSaveProvider = () => _currentSaveEditorVm;
                _navigator.Show(new BankView(bankVm, App.Services.GetRequiredService<SpriteService>()));
                break;
            case "dex":
                var dexBankVm = new BankViewModel(
                    App.Services.GetRequiredService<IBankService>(),
                    new SaveEngine());
                dexBankVm.CurrentSaveProvider = () => _currentSaveEditorVm;
                _navigator.Show(new LivingDexView(dexBankVm, _currentSaveEditorVm, App.Services.GetRequiredService<SpriteService>()));
                break;
            case "park":
                var parkBankVm = new BankViewModel(
                    App.Services.GetRequiredService<IBankService>(),
                    new SaveEngine());
                parkBankVm.CurrentSaveProvider = () => _currentSaveEditorVm;
                _navigator.Show(new ParkView(
                    App.Services.GetRequiredService<ParkStore>(),
                    App.Services.GetRequiredService<SpriteService>(),
                    parkBankVm,
                    _currentSaveEditorVm));
                break;
            case "events":
                if (_currentBankVm is null) _currentBankVm = new BankViewModel(App.Services.GetRequiredService<IBankService>(), new SaveEngine());
                _navigator.Show(new EventsView(_currentSaveEditorVm, _currentBankVm, App.Services.GetRequiredService<IEventDatabaseService>(), App.Services.GetRequiredService<SpriteService>()));
                break;
            case "about":
                _navigator.Show(new AboutView());
                break;
        }
    }
}
