using Avalonia;
using Avalonia.Headless;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// One headless (Skia-backed) Avalonia platform for the whole test assembly:
/// AppBuilder.Setup can only be called once per process, so every test class
/// must share this guard instead of bootstrapping on its own.
/// </summary>
public static class HeadlessTestPlatform
{
    private static readonly object Gate = new();
    private static bool _ready;

    public static void Ensure()
    {
        if (_ready) return;
        lock (Gate)
        {
            if (_ready) return;
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
            _ready = true;
        }
    }
}
