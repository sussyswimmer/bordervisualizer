using Xunit;

namespace Rimlight.Platform.Tests;

/// <summary>
/// Tests that wait on the thread pool (a pipe listener, a flush timer) run on their own, after the parallel classes:
/// while those keep every pool thread busy, a callback can wait more than a second for one, which would fail a check
/// that a start is served at once or let one that waits for a timer pass without it having run.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SerialTests
{
    public const string Name = "Waits on the thread pool";
}
