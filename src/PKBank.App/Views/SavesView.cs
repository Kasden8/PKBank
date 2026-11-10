using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKHeX.Core;
using SpeciesNameAlias = PKHeX.Core.SpeciesName;

namespace PKBank.App.Views;

/// <summary>
/// The save entry point: discover saves in the watched folders (emulators + manual),
/// then open one in the editor.
/// </summary>
public sealed class SavesView : UserControl
{
    private readonly SaveEditorViewModel _vm;
    private readonly SpriteService _sprites;
    private readonly TextBlock _status = new() { FontSize = 12.5 };
    private readonly StackPanel _list = new() { Spacing = 6 };

    public SavesView(SaveEditorViewModel vm, SpriteService sprites)
    {
        _vm = vm;
        _sprites = sprites;
        Background = Brushes.Transparent;
        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);

        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(PkChrome.PixelLabel("SAVES", 24, UiTokens.Bright));
        header.Children.Add(PkChrome.BodyLabel("Saves found by PKBank in its watched folders.", 12.5, UiTokens.InkSoft));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var open = new Button { Content = "Open a save file…" };
        SaveEditorView.StyleButton(open);
        open.Click += (_, _) => { _ = PickAndLoadAsync(); };
        var link = new Button { Content = "Link folder" };
        SaveEditorView.StyleButton(link);
        link.Click += (_, _) => { _ = LinkFolderAsync(); };
        var rescan = new Button { Content = "Rescan folders" };
        SaveEditorView.StyleButton(rescan);
        rescan.Click += (_, _) => ScanWatchedFolders();
        buttons.Children.Add(open);
        buttons.Children.Add(link);
        buttons.Children.Add(rescan);

        var root = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(26, 22),
            Children =
            {
                header,
                buttons,
                new ScrollViewer { Content = _list },
                _status,
            },
        };
        Content = root;
        ScanWatchedFolders();
    }

    private void ScanWatchedFolders()
    {
        _list.Children.Clear();
        var folders = WatchedRootStore.GetWatchedFolders();
        if (folders.Count == 0)
        {
            _list.Children.Add(PkChrome.BodyLabel("No folders linked yet. Link an emulator's save folder, or open a save file directly.", 12.5, UiTokens.InkSoft, wrap: true));
            _status.Text = "Link a folder to see your saves here.";
            return;
        }

        var anyFound = false;
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder.Path)) continue;
            var files = Directory.EnumerateFiles(folder.Path, "*", SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                if (!SaveFileProbe.LooksLikeSave(file)) continue;
                anyFound = true;
                AddRow(file, folder.Label ?? folder.Path);
            }
        }
        _status.Text = anyFound
            ? $"{_list.Children.Count} save(s) found. Pick one to open it in the editor."
            : "No saves recognized in those folders yet (open a save file directly if needed).";
    }

    private void AddRow(string path, string sourceLabel)
    {
        string info;
        try
        {
            var save = SaveUtil.GetSaveFile(File.ReadAllBytes(path));
            info = save is null ? "Parses as a save" : $"{save.Context} · Gen {save.Generation} · trainer {save.OT} · {save.BoxCount} boxes";
        }
        catch (Exception error)
        {
            info = "Unreadable (" + error.Message + ")";
        }

        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children =
            {
                new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        PkChrome.BodyLabel(Path.GetFileName(path), 13.5, UiTokens.Ink0),
                        PkChrome.BodyLabel(info, 11.5, UiTokens.InkSoft),
                        PkChrome.BodyLabel("from " + sourceLabel, 11, UiTokens.InkSoft),
                    },
                },
                OpenButton(path),
            },
        };
        Grid.SetColumn(grid.Children[1], 1);
        var row = new Border
        {
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 6),
            Background = PkChrome.PanelGradient(),
            BorderBrush = PkChrome.Brush(UiTokens.Rim),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(8),
            Child = grid,
        };
    }

    private Button OpenButton(string path)
    {
        var button = new Button { Content = "Open" };
        SaveEditorView.StyleButton(button);
        button.Click += async (_, _) =>
        {
            _status.Text = await _vm.LoadAsync(path) ?? "Opened " + Path.GetFileName(path);
            if (_vm.Save is not null)
            {
                var navigator = App.Services.GetService(typeof(Navigation.Navigator)) as Navigation.Navigator;
                navigator?.Show(new SaveEditorView(_vm, _sprites));
            }
        };
        return button;
    }

    private async Task LinkFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Link a saves folder",
            AllowMultiple = false,
        });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        WatchedRootStore.AddFolder(new WatchedRootStore.Folder(path, Path.GetFileName(path)));
        ScanWatchedFolders();
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
            _status.Text = error;
            return;
        }
        var navigator = App.Services.GetService(typeof(Navigation.Navigator)) as Navigation.Navigator;
        navigator?.Show(new SaveEditorView(_vm, _sprites));
    }
}
