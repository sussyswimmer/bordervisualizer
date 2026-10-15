using Rimlight.App.Logging;
using Xunit;

namespace Rimlight.Platform.Tests;

// RollingFileLog (K6, doc 02 "Logging"): every Trace line in %APPDATA%\Rimlight\logs, at most five files of 1 MB.
[Collection(SerialTests.Name)]
public sealed class RollingFileLogTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rimlight-k10-logs-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    [Fact]
    public void LinesAreQueuedThenWrittenWithATimeAndThread()
    {
        using var log = new RollingFileLog(directory);
        log.WriteLine("[Test] one");
        log.Write("[Test] ");
        log.WriteLine("two"); // Trace.Write without a line end joins the next line
        Assert.False(File.Exists(log.FilePath)); // nothing waits for the disk
        log.Flush();
        string[] lines = File.ReadAllLines(log.FilePath);
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3} \[ ?\d+\] \[Test\] one$", lines[0]);
        Assert.EndsWith("[Test] two", lines[1]);
        Thread.Sleep(700); // past the flush timer, which the first line armed for 500 ms
        Assert.Equal(2, File.ReadAllLines(log.FilePath).Length); // it found the queue empty and wrote nothing twice
    }

    [Fact]
    public void ItKeepsFiveFilesOfAtMostOneMegabyte()
    {
        using var log = new RollingFileLog(directory);
        string line = new('x', 1000);
        for (int batch = 0; batch < 8; batch++)
        {
            for (int i = 0; i < 1000; i++) log.WriteLine($"{batch} {line}"); // about 1 MB per batch
            log.Flush();
        }
        string[] files = Directory.GetFiles(directory).Select(Path.GetFileName).Order().ToArray()!;
        Assert.Equal(new[] { "rimlight.1.log", "rimlight.2.log", "rimlight.3.log", "rimlight.4.log", "rimlight.log" }, files);
        foreach (string file in files) Assert.InRange(new FileInfo(Path.Combine(directory, file)).Length, 1, RollingFileLog.MaxFileBytes);
        // The newest lines are in rimlight.log, the oldest kept ones in rimlight.4.log.
        Assert.StartsWith("7 ", Text(File.ReadAllLines(log.FilePath)[^1]), StringComparison.Ordinal);
        Assert.True(Text(File.ReadAllLines(Path.Combine(directory, "rimlight.4.log"))[0])[0] < '7');
    }

    [Fact]
    public void ARunawayLineIsCutAndAFailingDiskIsSurvived()
    {
        using (var log = new RollingFileLog(directory))
        {
            log.WriteLine(new string('y', 100_000));
            log.Flush();
            string written = File.ReadAllText(log.FilePath);
            Assert.InRange(written.Length, 32 * 1024, 32 * 1024 + 100);
            Assert.Contains("… (cut)", written, StringComparison.Ordinal);
        }

        // A folder that can't be created (a file is in the way): lines are dropped, nothing throws.
        string blocked = Path.Combine(directory, "blocked");
        File.WriteAllText(blocked, "");
        using var failing = new RollingFileLog(Path.Combine(blocked, "logs"));
        failing.WriteLine("lost");
        failing.Flush();
        failing.WriteLine("and the next line is queued as usual");
    }

    // A line without its time and thread: "2026-10-15 09:30:00.000 [ 5] text".
    private static string Text(string line) => line[(line.IndexOf("] ", StringComparison.Ordinal) + 2)..];
}
