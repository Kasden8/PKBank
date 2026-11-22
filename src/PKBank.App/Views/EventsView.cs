using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKForge.Domain;
using PKForge.Engine;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>
/// The offline Mystery Gift database: browse cards the open save could receive, put the
/// PokǸmon card into the Bank, or inject it into the open save (marked dirty thereafter).
/// </summary>
public sealed class EventsView : UserControl
{
    private readonly BankViewModel _bank;
    private readonly SaveEditorViewModel? _saveEditorVm;
    private readonly IEventDatabaseService _events;
    private readonly SpriteService _sprites;
    private readonly TextBlock _status = new() { FontSize = 12.5, Foreground = Avalonia.Media.Brushes.Gray };
    private readonly TextBox _search = new() { MinWidth = 240, PlaceholderText = "Search gift title / species…" };
    private readonly StackPanel _list = new() { Spacing = 6 };
    private readonly StackPanel _notice = new() { Spacing = 6 };
    private IReadOnlyList<EventGift> _all = [];
    private SaveFile? _albumSource;

    public EventsView(SaveEditorViewModel? saveEditorVm, BankViewModel bank, IEventDatabaseService events, SpriteService sprites)
    {
        _saveEditorVm = saveEditorVm;
        _bank = bank;
        _events = events;
        _sprites = sprites;
        Background = Brushes.Transparent;

        _search.TextChanged += (_, _) => Render();
        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);

        var root = new StackPanel { Spacing = 12, Margin = new Thickness(26, 22) };
        root.Children.Add(PkChrome.PixelLabel("EVENTS — Mystery Gift database", 20, UiTokens.Bright));
        root.Children.Add(_notice);
        root.Children.Add(_search);
        root.Children.Add(new ScrollViewer { Content = _list });
        root.Children.Add(_status);
        Content = root;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        if (_saveEditorVm?.Save is null)
        {
            _list.Children.Add(PkChrome.BodyLabel("Open a save first — gifts match your game's context.", 12.5, UiTokens.InkSoft));
            return;
        }

        if (!ReferenceEquals(_saveEditorVm.Save, _albumSource))
        {
            // The save changed: load its generation's gallery (a no-op when already
            // loaded) and rebuild the album. This also re-serves a genuinely empty album.
            EventArchive.EnsureLoaded(_saveEditorVm.Save.Generation);
            var bytes = _saveEditorVm.Save.Write(BinaryExportSetting.None).ToArray();
            using var session = new SaveEngineSession(bytes);
            _all = _events.GetGifts(session);
            _albumSource = _saveEditorVm.Save;
        }

        var needle = (_search.Text ?? "").Trim().ToLowerInvariant();
        var shown = _all.Where(g => needle.Length == 0
            || g.Title.Contains(needle, StringComparison.OrdinalIgnoreCase)
            || (g.Species > 0 && SpeciesName.GetSpeciesName((ushort)g.Species, (int)LanguageID.English).Contains(needle, StringComparison.OrdinalIgnoreCase))).Take(500);
        foreach (var gift in shown)
            _list.Children.Add(Row(gift));
        _status.Text = $"{_all.Count} cards for this game's context";
    }

    private Control Row(EventGift gift)
    {
        var icon = gift.Kind == EventGiftKind.Pokemon && gift.Species > 0
            ? _sprites.GetSprite(new SpriteLook(gift.Species, gift.Details?.Form ?? 0, gift.Shiny))
            : null;
        Control left = icon is null
            ? PkChrome.BodyLabel("◆", 16, UiTokens.InkSoft)
            : new Image { Source = icon, Width = 44, Height = 44, Stretch = Stretch.Uniform };

        var title = PkChrome.BodyLabel(gift.Title ?? SpeciesName.GetSpeciesName((ushort)gift.Species, (int)LanguageID.English), 12.5, UiTokens.Ink0, wrap: true);
        var meta = PkChrome.BodyLabel($"{gift.Details?.SourceFile ?? ""} · Lv. {gift.Level} · {gift.Details?.Format ?? ""} · Gen {gift.Generation}", 11, UiTokens.InkSoft);
        var text = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { new StackPanel { Spacing = 2, Children = { title, meta } } } };
        text.Children.Add(left);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (gift.Kind == EventGiftKind.Pokemon)
        {
            var toBank = new Button { Content = "To Bank" };
            SaveEditorView.StyleButton(toBank);
            toBank.Click += async (_, _) => _status.Text = await DepositToBankAsync(gift);
            buttons.Children.Add(toBank);
        }

        var inject = new Button { Content = "Inject to save" };
        SaveEditorView.StyleButton(inject);
        inject.Click += async (_, _) => _status.Text = await InjectGiftAsync(gift);
        buttons.Children.Add(inject);

        var row = new Border
        {
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 6),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(8),
            Child = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                Children = { text, buttons },
            },
        };
        Grid.SetColumn(buttons, 1);
        return row;
    }

    private async ValueTask<string> DepositToBankAsync(EventGift gift)
    {
        if (_saveEditorVm?.Save is null) return "Open a save first.";
        var bytes = _saveEditorVm.Save.Write(BinaryExportSetting.None).ToArray();
        using var session = new SaveEngineSession(bytes);
        var export = _events.ExportEntity(session, gift.Id);
        if (export is null) return "Could not convert this card for your save.";
        var info = _saveEditorVm.DescribeEntityForBank(export.Data, SpeciesName.GetSpeciesName((ushort)gift.Species, (int)LanguageID.English));
        if (info is null) return "Could not describe the card.";
        var outcome = await _bank.DepositAsync(export.Data, info!, releaseFromSave: null);
        return outcome;
    }

    private ValueTask<string> InjectGiftAsync(EventGift gift)
    {
        if (_saveEditorVm?.Save is null) return ValueTask.FromResult("Open a save first.");

        var bytes = _saveEditorVm.Save.Write(BinaryExportSetting.None).ToArray();
        using var session = new SaveEngineSession(bytes);

        // Games with a wonder card album (Gen 4-7): the card goes into the album and the
        // game's own delivery man hands the gift over, exactly like a real distribution.
        if (_events.StoresWonderCards(session))
        {
            var card = _events.ReceiveCard(session, gift.Id);
            if (!card.Success) return ValueTask.FromResult(card.Message);
            var rewritten = session.Serialize().ToArray();
            _saveEditorVm.ReplaceLoadedBytes(rewritten, "Wonder card in the album — Save to persist, then receive it in the game.");
            ShowSavePrompt(card.Message);
            return ValueTask.FromResult(card.Message);
        }

        // No album (Gen 3, Gen 8+, hackfors): the gift is written directly.
        if (gift.Kind == EventGiftKind.Item)
        {
            var items = _events.ReceiveItems(session, gift.Id);
            if (!items.Success) return ValueTask.FromResult(items.Message);
            var rewritten = session.Serialize().ToArray();
            _saveEditorVm.ReplaceLoadedBytes(rewritten, "Items delivered — Save to persist.");
            ShowSavePrompt(items.Message);
            return ValueTask.FromResult(items.Message);
        }

        var (box, slot) = FirstFreeSlot();
        var outcome = _events.Receive(session, gift.Id, box, slot);
        if (!outcome.Success) return ValueTask.FromResult(outcome.Message);
        var data = session.Serialize().ToArray();
        _saveEditorVm.ReplaceLoadedBytes(data, $"Gift {gift.Id} received — Save to persist.");
        ShowSavePrompt(outcome.Message);
        return ValueTask.FromResult(outcome.Message);
    }

    /// <summary>An injected gift only exists in memory until the save is written, and the
    /// write fails while an emulator holds the file open. A banner at the top of this
    /// screen says so and offers the write, instead of leaving the player wondering
    /// why nothing happened.</summary>
    private void ShowSavePrompt(string message)
    {
        _notice.Children.Clear();

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(PkChrome.PixelLabel("WONDER CARD INJECTED — SAVE PENDING", 15, UiTokens.Bright));
        panel.Children.Add(PkChrome.BodyLabel(message, 12.5, UiTokens.Ink0, wrap: true));
        panel.Children.Add(PkChrome.BodyLabel(
            "The save file has NOT been written yet. If your emulator is running, close it first: it keeps the save open and would overwrite this change when it closes.",
            11.5, UiTokens.Warn, wrap: true));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var save = new Button { Content = "Save now" };
        var dismiss = new Button { Content = "Save later" };
        SaveEditorView.StyleButton(save);
        SaveEditorView.StyleButton(dismiss);
        save.Background = PkChrome.Brush(UiTokens.MenuBlue);

        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            _status.Text = await SaveNowAsync();
            _notice.Children.Clear();
        };
        dismiss.Click += (_, _) =>
        {
            _notice.Children.Clear();
            _status.Text = "Card injected in memory only. Press Save in the editor to write it to disk.";
        };
        buttons.Children.Add(save);
        buttons.Children.Add(dismiss);
        panel.Children.Add(buttons);

        _notice.Children.Add(new Border
        {
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Warn),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12),
            Child = panel,
        });
    }

    private async ValueTask<string> SaveNowAsync()
    {
        if (_saveEditorVm is null) return "No save open.";
        var (_, message) = await _saveEditorVm.SaveAsync();
        return message;
    }

    /// <summary>The current box first, then the rest; (box, 0) when everything is full —
    /// the engine rejects the write and the user sees the message.</summary>
    private (int Box, int Slot) FirstFreeSlot()
    {
        var total = Math.Max(1, _saveEditorVm!.BoxTotal);
        for (var offset = 0; offset < total; offset++)
        {
            var box = (_saveEditorVm.BoxIndex + offset) % total;
            var slot = Enumerable.Range(0, 30).FirstOrDefault(s => _saveEditorVm.Slot(box, s).Species == 0, -1);
            if (slot >= 0) return (box, slot);
        }
        return (_saveEditorVm.BoxIndex, 0);
    }
}
