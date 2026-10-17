using System.Collections.Concurrent;
using System.Diagnostics;

namespace Rimlight.App.Tests;

/// <summary>
/// Records the app's <see cref="Trace"/> lines, which the app writes to its log (<c>RollingFileLog</c>). The windows
/// catch and log their own failures so the app keeps running ("[Settings] Opening the AppearancePage failed: …"), so
/// a smoke test that only watched for exceptions would pass while Settings showed nothing; it checks these lines too.
/// </summary>
internal sealed class TraceRecorder : TraceListener
{
    // The areas of the windows under test and of the app around them (the area is how every line starts).
    private static readonly string[] Areas = ["[Settings]", "[UiTheme]", "[Visualizer]", "[App]"];

    // How the app words a failure in those areas: "… failed: …", "… can't open …", "The preview failed and stopped".
    private static readonly string[] FailureWords = ["fail", "can't", "unhandled", "exception", "not applied"];

    private static readonly TraceRecorder Instance = new();
    private static int installed;

    private readonly ConcurrentQueue<string> lines = new();

    private TraceRecorder()
        : base("Rimlight.App.Tests")
    {
    }

    /// <summary>Adds the recorder to <see cref="Trace.Listeners"/>, once.</summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref installed, 1) == 0) Trace.Listeners.Add(Instance);
    }

    /// <summary>Forgets what was recorded so far (a test starts clean).</summary>
    public static void Clear()
    {
        while (Instance.lines.TryDequeue(out _))
        {
        }
    }

    /// <summary>Of the lines since the last call (or <see cref="Clear"/>), the ones that report a failure.</summary>
    public static string[] TakeFailures()
    {
        var failures = new List<string>();
        while (Instance.lines.TryDequeue(out string? line))
        {
            if (IsFailure(line)) failures.Add(line);
        }
        return [.. failures];
    }

    /// <inheritdoc />
    public override void Write(string? message) => lines.Enqueue(message ?? "");

    /// <inheritdoc />
    public override void WriteLine(string? message) => lines.Enqueue(message ?? "");

    private static bool IsFailure(string line) =>
        Areas.Any(area => line.StartsWith(area, StringComparison.Ordinal))
        && FailureWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase));
}
