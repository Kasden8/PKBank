using Avalonia.Controls;

namespace PKBank.App.Navigation;

/// <summary>
/// The shell's page switcher: one content host, views are swapped in and out. Views are
/// built on demand (cheap, parameterless) and owned by the shell — no VM locator yet.
/// </summary>
public sealed class Navigator
{
    private Control _current = new Views.HomeView();

    public Control Current => _current;

    public event Action<Control>? ViewChanged;

    public void Show(Control view)
    {
        _current = view;
        ViewChanged?.Invoke(view);
    }

    public void ShowHome() => Show(new Views.HomeView());
}
