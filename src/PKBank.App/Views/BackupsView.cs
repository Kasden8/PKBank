using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PKForge.Domain;
using PKBank.App.Theme;
using PKBank.App.ViewModels;

namespace PKBank.App.Views;

/// <summary>
/// The restore-points browser: every backup per save, newest first
/// (PKForge's BackupHistoryPage equivalent, trimmed to the PC).
/// </summary>
public sealed class BackupsView : UserControl
{
    private readonly IBackupService _backups;
    private readonly SaveEditorViewModel? _saveEditorVm;
    private readonly TextBlock _status = new() { FontSize = 12.5 };
    private readonly StackPanel _list = new() { Spacing = 6 };

    public BackupsView(IBackupService backups, SaveEditorViewModel? saveEditorVm)
    {
        _backups = backups;
        _saveEditorVm = saveEditorVm;
        Background = Brushes.Transparent;
        _status.Foreground = PkChrome.Brush(UiTokens.InkSoft);

        var root = new StackPanel { Spacing = 14, Margin = new Thickness(26, 22) };
        root.Children.Add(PkChrome.PixelLabel("RESTORE POINTS", 24, UiTokens.Bright));
        root.Children.Add(PkChrome.BodyLabel("Every write keeps its previous state here. Restoring replaces the file on disk — always confirm the current state is saved.", 12.5, UiTokens.InkSoft));

        var refresh = new Button { Content = "Refresh" };
        SaveEditorView.StyleButton(refresh);
        refresh.Click += (_, _) => Render();
        root.Children.Add(refresh);
        root.Children.Add(new ScrollViewer { Content = _list });
        root.Children.Add(_status);
        Content = root;
        Render();
    }

    private void Render()
    {
        _list.Children.Clear();
        var entries = _backups.ListAsync().AsTask().GetAwaiter().GetResult();
        if (entries.Count == 0)
        {
            _list.Children.Add(PkChrome.BodyLabel("No restore points yet. Opening a save creates one automatically.", 13, UiTokens.InkSoft));
            return;
        }
        foreach (var entry in entries.OrderByDescending(e => e.CreatedUtc).Take(100))
        {
            var restore = new Button { Content = "Restore" };
            SaveEditorView.StyleButton(restore);
            restore.Click += async (_, _) => _status.Text = await RestoreAsync(entry);
            var row = new Border
            {
                Padding = new Thickness(12),
                Margin = new Thickness(0, 0, 0, 6),
                Background = PkChrome.PanelGradient(),
                BorderBrush = PkChrome.Brush(UiTokens.Rim),
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Child = new Grid
                {
                    ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                    Children =
                    {
                        new StackPanel
                        {
                            Spacing = 3,
                            Children =
                            {
                                PkChrome.BodyLabel(entry.DisplayName ?? "?", 13, UiTokens.Ink0),
                                PkChrome.BodyLabel($"{entry.CreatedUtc.ToLocalTime()} · v{entry.Format} · gen {entry.Generation} · {entry.Sha256[..12]}", 11.5, UiTokens.InkSoft),
                                PkChrome.BodyLabel(string.IsNullOrEmpty(entry.ChangeDescription) ? "" : "— " + entry.ChangeDescription, 11, UiTokens.InkSoft),
                            },
                        },
                        restore,
                    },
                },
            };
            Grid.SetColumn(restore, 1);
            _list.Children.Add(row);
        }
    }

    private async ValueTask<string> RestoreAsync(BackupInfo entry)
    {
        if (_saveEditorVm?.FilePath is null) return "Open a save first to take the restore point back into it.";
        var bytes = await _backups.ReadAsync(entry.BackupId);
        _saveEditorVm.ReplaceLoadedBytes(bytes.ToArray(), $"Restore point {entry.CreatedUtc.ToLocalTime()} loaded (Save to persist).");
        return $"Restore point {entry.CreatedUtc.ToLocalTime()} loaded (Save to persist).";
    }
}
