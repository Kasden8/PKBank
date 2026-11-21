using Xunit;

// The Avalonia headless platform binds its dispatcher to the thread that bootstraps it.
// Running test classes in parallel hands UI work to other threads, which Avalonia rejects
// with "The calling thread cannot access this object because a different thread owns it".
[assembly: CollectionBehavior(DisableTestParallelization = true)]
