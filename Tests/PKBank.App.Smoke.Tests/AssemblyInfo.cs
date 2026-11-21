using Avalonia.Headless;
using Xunit;

// The headless session starts the app through this entry point; HeadlessTestPlatform
// then runs every UI test body on the Avalonia dispatcher thread (the app's UI thread).
[assembly: AvaloniaTestApplication(typeof(PKBank.App.Smoke.Tests.TestAppBuilder))]

// One Avalonia session per assembly: parallel test classes would fight over the single
// headless application instance.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
