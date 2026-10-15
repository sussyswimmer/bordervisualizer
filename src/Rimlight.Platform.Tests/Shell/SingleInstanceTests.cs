using System.IO.Pipes;
using System.Text;
using Rimlight.Platform.Shell;
using Xunit;

namespace Rimlight.Platform.Tests;

// SingleInstance (K6, doc 06 §4): one copy per user session. A later start hands its request to the running copy over a
// pipe, and an exiting copy stops taking requests before its teardown, so a start then takes over instead of handing its
// request to a process that would drop it.
public sealed class SingleInstanceTests
{
    [Fact]
    public void AnExitingCopyTakesNoMoreRequestsAndReleasesItsClaimLast()
    {
        string app = "RimlightTests-" + Guid.NewGuid().ToString("N")[..12]; // never the real app's mutex or pipe
        var received = new List<string>();
        SingleInstance first = SingleInstance.Claim(app, command: null)!;
        Assert.NotNull(first);
        try
        {
            first.CommandReceived += command =>
            {
                lock (received) received.Add(command);
            };
            first.StartListening();
            Assert.True(TrySend(first.PipeName, SingleInstance.ShowSettingsCommand));
            Assert.True(SpinWait.SpinUntil(() => Count(received) == 1, TimeSpan.FromSeconds(5)));

            // Exiting: from now on a start finds no pipe, so its Claim waits for this process instead of handing over.
            first.StopListening();
            first.StopListening(); // twice is harmless
            Assert.False(TrySend(first.PipeName, SingleInstance.DemoCommand));
            Assert.Equal(1, Count(received));
        }
        finally
        {
            first.Dispose(); // the claim goes last, and still goes after StopListening
        }

        // Another thread (another start) can claim at once now.
        SingleInstance? next = null;
        var thread = new Thread(() =>
        {
            next = SingleInstance.Claim(app, command: null);
            next?.Dispose();
        });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(3)), "a claim after Dispose waited");
        Assert.NotNull(next);
    }

    // A later start's hand-over, without Send's Windows-only foreground call.
    private static bool TrySend(string pipeName, string command)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        try
        {
            client.Connect(300);
            client.Write(Encoding.UTF8.GetBytes(command + "\n"));
            client.Flush();
            if (OperatingSystem.IsWindows()) client.WaitForPipeDrain(); // as Send does: stay until it has been read
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            return false;
        }
    }

    private static int Count(List<string> received)
    {
        lock (received) return received.Count;
    }
}
