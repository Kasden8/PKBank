using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PKBank.App.Services;
using PKBank.App.Theme;
using PKBank.App.ViewModels;
using PKHeX.Core;

namespace PKBank.App.Views;

/// <summary>
/// The save entry point: the saves PKBank remembers (opened at least once) plus whatever
/// lives in the watched folders, so any save reopens in one click — no file dialog.
/// </summary>
public sealed class SavesView : UserControl
{
    private const int MaxScanDepth = 6;
    private const long MaxScanFileSize = 4 * 1024 * 1024;

    private readonly SaveEditorViewModel _vm;
    private readonly SpriteService _sprites;
    private readonly SaveRegistry _registry;
    private readonly TextBlock _status = new() { FontSize = 12.5 };
    private readonly StackPanel _list = new() { Spacing = 6 };

    public SavesView(SaveEditorViewModel vm, SpriteService sprites, SaveRegistry? registry = null)
    {
        _vm = vm;
        _sprites = sprites;
        _registry = registry ?? SaveRegistry.Default;
        Background = Brushes.Transparent;
        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);

        var header = new StackPanel { Spacing = 4 };
        header.Children.Add(PkChrome.PixelLabel("SAVES", 24, UiTokens.Bright));
        header.Children.Add(PkChrome.BodyLabel("Every save you have opened stays here. Open one in a click, or browse all of its boxes.", 12.5, UiTokens.InkSoft));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var open = new Button { Content = "Open a save file…" };
        SaveEditorView.StyleButton(open);
        open.Click += (_, _) => { _ = PickAndLoadAsync(); };
        var link = new Button { Content = "Link folder" };
        SaveEditorView.StyleButton(link);
        link.Click += (_, _) => { _ = LinkFolderAsync(); };
        var rescan = new Button { Content = "Rescan folders" };
        SaveEditorView.StyleButton(rescan);
        rescan.Click += (_, _) => Refresh();
        buttons.Children.Add(open);
        buttons.Children.Add(link);
        buttons.Children.Add(rescan);

        _list.MaxWidth = 1000;
        _list.HorizontalAlignment = HorizontalAlignment.Left;

        var root = new StackPanel
        {
            Spacing = 14,
            Margin = new Thickness(26, 22),
            Children =
            {
                header,
                buttons,
                new ScrollViewer
                {
                    Content = _list,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                },
                _status,
            },
        };
        Content = root;
        Refresh();
    }

    /// <summary>Rebuilds both lists: remembered saves first, then a scan of the watched folders.</summary>
    private void Refresh()
    {
        _list.Children.Clear();

        var registered = _registry.GetAll();
        _list.Children.Add(SectionLabel("REMEMBERED SAVES"));
        if (registered.Count == 0)
        {
            _list.Children.Add(PkChrome.BodyLabel("Nothing here yet — open a save file and it will stay listed.", 12.5, UiTokens.InkSoft, wrap: true));
        }
        else
        {
            foreach (var entry in registered) _list.Children.Add(RegisteredRow(entry));
        }

        _list.Children.Add(SectionLabel("IN WATCHED FOLDERS"));
        var found = ScanWatchedFolders();
        if (found == 0)
        {
            var folders = WatchedRootStore.GetWatchedFolders();
            _list.Children.Add(PkChrome.BodyLabel(
                folders.Count == 0
                    ? "No folders linked yet. Link an emulator's save folder, or open a save file directly."
                    : "No saves recognized in those folders yet (open a save file directly if needed).",
                12.5, UiTokens.InkSoft, wrap: true));
        }

        _status.Text = $"{registered.Count} remembered · {found} found in watched folders. Pick one to open it in the editor.";
    }

    /// <summary>A short path for the row (the full one lives in the tooltip).</summary>
    private static TextBlock PathLabel(string text, string fullPath)
    {
        var label = PkChrome.BodyLabel(text, 11, UiTokens.InkSoft);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        ToolTip.SetTip(label, fullPath);
        return label;
    }

    /// <summary>Keeps the tail of a long emulator path: "C:\…\00055e00\data\00000001\main".</summary>
    private static string ShortenPath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path) ?? "";
            var rest = path[root.Length..];
            var parts = rest.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 4) return path;
            return root + "…" + Path.DirectorySeparatorChar + string.Join(Path.DirectorySeparatorChar, parts[^3..]);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static Control SectionLabel(string caption)
    {
        var label = PkChrome.PixelLabel(caption, 13, UiTokens.Bright);
        label.Margin = new Thickness(0, 8, 0, 2);
        return label;
    }

    /// <summary>One remembered save, with its Open/Boxes buttons. Internal for the smoke tests.</summary>
    internal Border RegisteredRow(RegisteredSave entry)
    {
        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(PkChrome.BodyLabel($"{entry.GameLabel} · Gen {entry.Generation} · {entry.TrainerName}", 13.5, UiTokens.Ink0));
        body.Children.Add(PathLabel($"{entry.FileName} — {ShortenPath(entry.Path)}", entry.Path));
        body.Children.Add(PkChrome.BodyLabel($"{entry.BoxCount} boxes · opened {entry.LastOpened.LocalDateTime:g}", 11, UiTokens.InkSoft));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (entry.Exists)
        {
            var open = ActionButton("Open", () => { _ = OpenAsync(entry.Path, browseBoxes: false); });
            var boxes = ActionButton("Boxes", () => { _ = OpenAsync(entry.Path, browseBoxes: true); });
            actions.Children.Add(open);
            actions.Children.Add(boxes);
        }
        else
        {
            var missing = PkChrome.BodyLabel("File not found", 11.5, UiTokens.Bad, wrap: true);
            missing.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(missing);
            actions.Children.Add(ActionButton("Forget", () =>
            {
                _registry.Remove(entry.Path);
                Refresh();
            }));
        }

        return RowBorder(body, actions);
    }

    private int ScanWatchedFolders()
    {
        var found = 0;
        foreach (var folder in WatchedRootStore.GetWatchedFolders())
        {
            if (!Directory.Exists(folder.Path)) continue;
            foreach (var file in EnumerateFiles(folder.Path, MaxScanDepth))
            {
                try
                {
                    if (new FileInfo(file).Length > MaxScanFileSize) continue;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (!SaveFileProbe.LooksLikeSave(file)) continue;
                found++;
                _list.Children.Add(ScanRow(file, folder.Label ?? folder.Path));
            }
        }
        return found;
    }

    /// <summary>Breadth-first walk with a depth cap: one linked emulator root can cover every game.</summary>
    private static IEnumerable<string> EnumerateFiles(string root, int maxDepth)
    {
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (directory, depth) = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { continue; }
            foreach (var file in files) yield return file;
            if (depth >= maxDepth) continue;
            string[] children;
            try { children = Directory.GetDirectories(directory); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { continue; }
            foreach (var child in children) pending.Push((child, depth + 1));
        }
    }

    private Border ScanRow(string path, string sourceLabel)
    {
        string info;
        try
        {
            var save = SaveUtil.GetSaveFile(File.ReadAllBytes(path));
            info = save is null
                ? "Parses as a save"
                : $"{GameInfo.GetVersionName(save.Version)} · Gen {save.Generation} · trainer {save.OT} · {save.BoxCount} boxes";
        }
        catch (Exception error)
        {
            info = "Unreadable (" + error.Message + ")";
        }

        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(PkChrome.BodyLabel(Path.GetFileName(path), 13.5, UiTokens.Ink0));
        body.Children.Add(PkChrome.BodyLabel(info, 11.5, UiTokens.InkSoft));
        body.Children.Add(PathLabel("from " + sourceLabel + " · " + ShortenPath(path), path));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(ActionButton("Open", () => { _ = OpenAsync(path, browseBoxes: false); }));
        actions.Children.Add(ActionButton("Boxes", () => { _ = OpenAsync(path, browseBoxes: true); }));
        return RowBorder(body, actions);
    }

    private static Border RowBorder(Control body, Control actions)
    {
        var grid = new Grid
        {
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
            Children = { body, actions },
        };
        Grid.SetColumn(actions, 1);
        return new Border
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

    private static Button ActionButton(string caption, Action action)
    {
        var button = new Button { Content = caption };
        SaveEditorView.StyleButton(button);
        button.Click += (_, _) => action();
        return button;
    }

    private async Task OpenAsync(string path, bool browseBoxes)
    {
        if (_vm.Dirty)
        {
            _status.Text = "You have unsaved changes — save them in the editor before switching saves.";
            return;
        }
        if (!File.Exists(path))
        {
            _status.Text = "The save moved or was deleted: " + path;
            Refresh();
            return;
        }
        string? error;
        try { error = await _vm.LoadAsync(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = "Could not read the save: " + ex.Message;
            return;
        }
        if (error is not null)
        {
            _status.Text = error;
            return;
        }
        var navigator = Navigator();
        if (navigator is null) return;
        navigator.Show(browseBoxes
            ? new BoxBrowserView(_vm, _sprites)
            : new SaveEditorView(_vm, _sprites));
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
        Refresh();
    }

    private async Task PickAndLoadAsync()
    {
        if (_vm.Dirty)
        {
            _status.Text = "You have unsaved changes — save them in the editor before switching saves.";
            return;
        }
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
        Navigator()?.Show(new SaveEditorView(_vm, _sprites));
    }

    /// <summary>Loads a save whose path arrived from the command line and jumps into the editor.</summary>
    public async Task OpenPathAsync(string path)
    {
        var error = await _vm.LoadAsync(path);
        if (error is not null)
        {
            _status.Text = error;
            return;
        }
        Navigator()?.Show(new SaveEditorView(_vm, _sprites));
    }

    private static Navigation.Navigator? Navigator() =>
        App.Services?.GetService(typeof(Navigation.Navigator)) as Navigation.Navigator;
}
