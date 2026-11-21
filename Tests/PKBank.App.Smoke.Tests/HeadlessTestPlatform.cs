using Avalonia;
using Avalonia.Headless;

namespace PKBank.App.Smoke.Tests;

/// <summary>
/// One headless (Skia-backed) Avalonia session for the whole test assembly.
/// <para>
/// Avalonia's dispatcher belongs to the thread that started the application, and the
/// controls only tolerate being touched from it. xUnit runs each test on a pool thread,
/// so every test that touches the UI runs its body through <see cref="Run(Action)"/>,
/// which hops onto the Avalonia UI thread. Without it the failures are random:
/// "The calling thread cannot access this object because a different thread owns it".
/// </para>
/// </summary>
public static class HeadlessTestPlatform
{
    private static readonly Lock Gate = new();
    private static HeadlessUnitTestSession? _session;

    /// <summary>Runs the body on the Avalonia UI thread and waits for it to finish.</summary>
    public static void Run(Action body)
    {
        var session = Session();
        session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Runs the body on the Avalonia UI thread and returns its result.</summary>
    public static T Run<T>(Func<T> body)
    {
        var session = Session();
        return session.Dispatch(body, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static HeadlessUnitTestSession Session()
    {
        lock (Gate)
            return _session ??= HeadlessUnitTestSession.GetOrStartForAssembly(typeof(HeadlessTestPlatform).Assembly);
    }
}
