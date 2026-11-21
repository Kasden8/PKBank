using Avalonia;
using Avalonia.Headless;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// The application the headless test session starts. The suite drives the real
/// <see cref="App"/> (so the views see the same code) on a headless, Skia-backed platform.
/// </summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
