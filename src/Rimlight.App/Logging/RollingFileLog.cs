using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Rimlight.App.Logging;

/// <summary>
/// A trace listener that writes every <see cref="Trace"/> line to <c>rimlight.log</c> and keeps at most five files of
/// 1 MB each (doc 02 "Logging"): <c>rimlight.log</c>, then <c>rimlight.1.log</c> (newest) to <c>rimlight.4.log</c>.
/// </summary>
/// <remarks>
/// Writing a line only queues it, so a caller on the overlay or audio thread never waits for the disk. A pool timer
/// writes the queue 500 ms after the first new line, opening the file for each batch (so it can be read or deleted
/// at any time). The listener never throws: lines that can't be written are dropped. The app logs only events and
/// rate-limited errors, never per frame.
/// </remarks>
internal sealed class RollingFileLog : TraceListener
{
    /// <summary>A file is rolled over before it would grow past this.</summary>
    public const long MaxFileBytes = 1024 * 1024;

    /// <summary>The current file plus the rolled-over ones.</summary>
    public const int MaxFiles = 5;

    private const int FlushDelayMs = 500;
    private const int MaxLineChars = 32 * 1024; // a runaway message can't fill a file on its own
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    [ThreadStatic]
    private static StringBuilder? partialLine; // Trace.Write without a line end (TraceEvent headers), per thread

    private readonly string directory;
    private readonly string baseName;
    private readonly ConcurrentQueue<string> pending = new();
    private readonly Timer flushTimer;
    private readonly object writeGate = new(); // one batch at a time; never taken by a caller of Trace
    private int flushScheduled;
    private int disposed;

    /// <summary>Creates the listener; the folder is created on the first write.</summary>
    /// <param name="directory">The logs folder.</param>
    /// <param name="fileName">The current file's name; rolled-over files insert .1 to .4 before the extension.</param>
    public RollingFileLog(string directory, string fileName = "rimlight.log")
    {
        this.directory = directory;
        baseName = Path.GetFileNameWithoutExtension(fileName);
        FilePath = Path.Combine(directory, fileName);
        flushTimer = new Timer(_ => WritePending(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The current log file.</summary>
    public string FilePath { get; }

    /// <inheritdoc />
    public override bool IsThreadSafe => true;

    /// <inheritdoc />
    public override void Write(string? message) => (partialLine ??= new StringBuilder()).Append(message);

    /// <inheritdoc />
    public override void WriteLine(string? message)
    {
        if (Volatile.Read(ref disposed) != 0) return;
        string text = message ?? "";
        if (partialLine is { Length: > 0 } partial)
        {
            text = partial.Append(text).ToString();
            partial.Clear();
        }
        if (text.Length > MaxLineChars) text = string.Concat(text.AsSpan(0, MaxLineChars), " … (cut)");
        pending.Enqueue(string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,2}] {text}{Environment.NewLine}"));
        if (Interlocked.Exchange(ref flushScheduled, 1) != 0) return;
        try
        {
            flushTimer.Change(FlushDelayMs, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Disposed meanwhile (exit); a Trace call must never fail because of the log.
        }
    }

    /// <summary>Writes everything queued now (on exit, before opening the folder, after an unhandled exception).</summary>
    public override void Flush() => WritePending();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0)
        {
            flushTimer.Dispose();
            WritePending();
        }
        base.Dispose(disposing);
    }

    private void WritePending()
    {
        lock (writeGate)
        {
            // Cleared first: a line queued from now on schedules another write.
            Volatile.Write(ref flushScheduled, 0);
            if (pending.IsEmpty) return;
            FileStream? stream = null;
            try
            {
                Directory.CreateDirectory(directory);
                stream = Open();
                long size = stream.Length;
                while (pending.TryDequeue(out string? line))
                {
                    byte[] bytes = Utf8.GetBytes(line);
                    if (size > 0 && size + bytes.Length > MaxFileBytes)
                    {
                        // Rolled over line by line, so a burst can't grow one file past the limit.
                        stream.Dispose();
                        RollOver();
                        stream = Open();
                        size = 0;
                    }
                    stream.Write(bytes);
                    size += bytes.Length;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Best effort: the rest of the batch is dropped (the debugger output still has it). Not traced, which
                // would queue another line and retry a failing disk forever.
                pending.Clear();
            }
            finally
            {
                stream?.Dispose();
            }
        }
    }

    // Opened per batch and shared, so the file can be read, copied or deleted while the app runs.
    private FileStream Open() => new(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

    // rimlight.3.log → .4, … rimlight.log → .1; the oldest goes.
    private void RollOver()
    {
        File.Delete(Numbered(MaxFiles - 1));
        for (int i = MaxFiles - 2; i >= 1; i--)
        {
            if (File.Exists(Numbered(i))) File.Move(Numbered(i), Numbered(i + 1));
        }
        File.Move(FilePath, Numbered(1));
    }

    private string Numbered(int index) =>
        Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{baseName}.{index}{Path.GetExtension(FilePath)}"));
}
