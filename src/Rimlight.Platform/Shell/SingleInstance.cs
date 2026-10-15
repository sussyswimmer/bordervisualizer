using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.System.Threading;

namespace Rimlight.Platform.Shell;

/// <summary>
/// Keeps one copy of the app per user session (doc 06 §4): a named mutex <c>Local\{App}-{user SID}</c> decides which
/// process runs, and a named pipe lets a later start hand its request ("show-settings") to the running one. The pipe
/// accepts only the current user and only short, known-length commands.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Asks the running instance to open its settings window.</summary>
    public const string ShowSettingsCommand = "show-settings";

    private const int MaxCommandBytes = 64;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan TakeOverTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PipeRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(1);

    private readonly Mutex? mutex; // null when the mutex couldn't be created: the app runs unguarded
    private readonly string pipeName;
    private readonly CancellationTokenSource stop = new();
    private Task? listener;
    private bool disposed;

    private SingleInstance(Mutex? mutex, string pipeName)
    {
        this.mutex = mutex;
        this.pipeName = pipeName;
    }

    /// <summary>
    /// Raised on the thread pool with each command a later start sends (e.g. <see cref="ShowSettingsCommand"/>).
    /// Handlers must marshal to the UI thread themselves.
    /// </summary>
    public event Action<string>? CommandReceived;

    /// <summary>
    /// Makes this process the running instance, or hands <paramref name="command"/> to the one already running.
    /// Call before creating anything another instance would clash with (the tray icon). Never throws.
    /// </summary>
    /// <param name="appName">The app's name, the first part of the mutex and pipe names.</param>
    /// <param name="command">What to ask of a running instance, or null to just leave quietly (a start at sign-in).</param>
    /// <param name="runningElsewhere">
    /// The exe of the running instance when it is another copy of the app (another build or folder): the command is
    /// not sent, and the caller should say where the app runs instead of exiting without a word. Otherwise null.
    /// </param>
    /// <param name="runningAtOtherElevation">
    /// True when the running instance runs at the other elevation: one of the two was started as administrator. The
    /// mutex still keeps this process out, but the pipe turns its command away (only the token owner that created a
    /// pipe may use it: Administrators for an elevated process, the user otherwise), so the caller should say so instead
    /// of exiting without a word. False otherwise, and always for a start without a command, which just leaves.
    /// </param>
    /// <returns>The claim for this process (dispose it on exit), or null when another instance runs and this one should exit.</returns>
    public static SingleInstance? Claim(string appName, string? command, out string? runningElsewhere, out bool runningAtOtherElevation)
    {
        runningElsewhere = null;
        runningAtOtherElevation = false;
        string user = CurrentUserSid();
        // Local\ is per session, the pipe namespace is not: the session ID keeps two sessions of one user apart.
        string pipeName = $"{appName}-{user}-{SessionId()}";
        Mutex mutex;
        try
        {
            mutex = new Mutex(false, $@"Local\{appName}-{user}");
        }
        catch (UnauthorizedAccessException)
        {
            // A fallback. Across elevations the mutex normally opens (a copy started as administrator still lets the
            // user's logon session wait on it, which is all this open asks for) and it is the pipe that turns a start
            // away (Send). Should Windows refuse the mutex itself, a copy at the other elevation holds it: running
            // anyway would draw the glow twice and take that copy's tray icon (H.NotifyIcon derives the icon's ID from
            // the exe path), leaving it without a way to quit.
            Trace.WriteLine("[SingleInstance] The mutex exists but may not be opened (a copy at the other elevation holds it); exiting.");
            runningAtOtherElevation = command is not null;
            return null;
        }
        catch (Exception exception)
        {
            // Better two copies than none.
            Trace.WriteLine($"[SingleInstance] The mutex could not be created, running without it: {exception.Message}");
            return new SingleInstance(null, pipeName);
        }

        if (TryOwn(mutex, TimeSpan.Zero)) return new SingleInstance(mutex, pipeName);
        if (command is not null && Send(pipeName, command, out runningElsewhere, out runningAtOtherElevation))
        {
            mutex.Dispose();
            return null;
        }
        // The running instance didn't take the request: it may be exiting (an update restarting it). Take over once
        // it is gone; otherwise leave it running.
        if (TryOwn(mutex, TakeOverTimeout)) return new SingleInstance(mutex, pipeName);
        Trace.WriteLine("[SingleInstance] Another instance is running and did not answer; exiting.");
        mutex.Dispose();
        return null;
    }

    /// <summary>Starts accepting commands from later starts. Call once the app can handle them.</summary>
    public void StartListening()
    {
        if (listener is not null || stop.IsCancellationRequested) return;
        CancellationToken token = stop.Token;
        listener = Task.Run(() => ListenAsync(token));
    }

    /// <summary>
    /// Stops accepting commands but keeps the claim. Call first when the app starts to exit: a start from then on finds
    /// no pipe, waits for the mutex and takes over, instead of handing its request to a process that is going away.
    /// </summary>
    public void StopListening()
    {
        if (stop.IsCancellationRequested) return;
        stop.Cancel();
        try
        {
            listener?.Wait(StopTimeout);
        }
        catch (AggregateException)
        {
            // Logged by the loop; nothing more to do on exit.
        }
    }

    /// <summary>Stops listening and releases the mutex. Call on the thread that called <see cref="Claim"/>.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        StopListening();
        if (mutex is null) return;
        try
        {
            mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not the owning thread; the mutex is released when the process ends.
        }
        mutex.Dispose();
    }

    private static bool TryOwn(Mutex mutex, TimeSpan timeout)
    {
        try
        {
            return mutex.WaitOne(timeout);
        }
        catch (AbandonedMutexException)
        {
            return true; // the previous owner crashed; the mutex is ours now
        }
    }

    // A later start: hands the command over and lets the running instance bring its window to the front. True when
    // it was delivered, when the running instance is another copy (runningElsewhere, nothing sent), or when its pipe
    // turned this process away (otherElevation, nothing sent): waiting for that copy to exit would only delay the
    // message.
    private static bool Send(string pipeName, string command, out string? runningElsewhere, out bool otherElevation)
    {
        runningElsewhere = null;
        otherElevation = false;
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
        try
        {
            client.Connect(ConnectTimeout); // waits while the running instance is still starting up
            if (PInvoke.GetNamedPipeServerProcessId(client.SafePipeHandle, out uint serverProcess))
            {
                // Another build (a dev build registered at sign-in, say) would open its own window and this start would
                // vanish, so the person would test the wrong copy. Hang up instead; the server reads nothing.
                if (ExePath(serverProcess) is { } server && Environment.ProcessPath is { } own
                    && !string.Equals(server, own, StringComparison.OrdinalIgnoreCase))
                {
                    runningElsewhere = server;
                    return true;
                }
                // Windows lets only the foreground process (this one, just launched by the user) hand the foreground on.
                PInvoke.AllowSetForegroundWindow(serverProcess);
            }
            client.Write(Encoding.UTF8.GetBytes(command + "\n"));
        }
        catch (UnauthorizedAccessException exception)
        {
            // The running copy is at the other elevation. Its pipe admits only the token owner that created it: from an
            // elevated copy that is Administrators, which a normal start holds for deny only, so Windows refuses the
            // connection; from a normal copy it is the user, which an elevated start (owner Administrators) won't
            // accept as its own. A message is the answer, not a pipe open to the other side.
            Trace.WriteLine($"[SingleInstance] The running instance runs at the other elevation: {exception.Message}");
            otherElevation = true;
            return true;
        }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            Trace.WriteLine($"[SingleInstance] Could not reach the running instance: {exception.Message}");
            return false;
        }
        try
        {
            // Stay until it has read the command (it hangs up after 2 s at the latest), so this process doesn't exit
            // and close the pipe first.
            client.WaitForPipeDrain();
        }
        catch (Exception)
        {
            // It read the command and hung up already; the command is delivered either way.
        }
        return true;
    }

    // The full path of a process's exe, or null if Windows won't say. Limited query rights work across users and
    // integrity levels.
    private static string? ExePath(uint processId)
    {
        using SafeFileHandle process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid) return null;
        Span<char> path = stackalloc char[1024];
        uint length = (uint)path.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, ref length)
            ? path[..(int)length].ToString()
            : null;
    }

    private async Task ListenAsync(CancellationToken token)
    {
        try
        {
            await ListenLoopAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[SingleInstance] Listening stopped: {exception}");
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        byte[] buffer = new byte[MaxCommandBytes];
        NamedPipeServerStream? server = null;
        bool unavailable = false; // the pipe couldn't be created, which was logged once
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (server is null)
                {
                    try
                    {
                        // One instance (FILE_FLAG_FIRST_PIPE_INSTANCE): creation fails if another process holds the name.
                        server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        // Another process holds the name for as long as it runs (a copy running without the mutex, say).
                        // Logged once, not on every retry, so the log keeps its history.
                        if (!unavailable)
                        {
                            unavailable = true;
                            Trace.WriteLine($"[SingleInstance] The pipe is not available ({exception.Message}); retrying every 5 s.");
                        }
                        if (!await Delay(PipeRetryDelay, token).ConfigureAwait(false)) return;
                        continue;
                    }
                    if (unavailable)
                    {
                        unavailable = false;
                        Trace.WriteLine("[SingleInstance] The pipe is available again.");
                    }
                }

                try
                {
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    if (await ReadCommandAsync(server, buffer, token).ConfigureAwait(false) is { } command) Raise(command);
                    // The same instance serves the next start, so a start waiting meanwhile never finds the name gone.
                    server.Disconnect();
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    // A client that vanished mid-way; start over with a fresh instance.
                    Trace.WriteLine($"[SingleInstance] Serving a start failed: {exception.Message}");
                    await server.DisposeAsync().ConfigureAwait(false);
                    server = null;
                }
            }
        }
        finally
        {
            if (server is not null) await server.DisposeAsync().ConfigureAwait(false);
        }
    }

    // Reads one line of at most MaxCommandBytes, giving up after ReadTimeout so a silent client can't block the pipe.
    private static async Task<string?> ReadCommandAsync(NamedPipeServerStream server, byte[] buffer, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(ReadTimeout);
        int length = 0;
        try
        {
            while (length < buffer.Length)
            {
                int read = await server.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
                if (Array.IndexOf(buffer, (byte)'\n', 0, length) >= 0) break;
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Trace.WriteLine("[SingleInstance] A client sent nothing within 2 s.");
            return null;
        }
        int end = Array.IndexOf(buffer, (byte)'\n', 0, length);
        string command = Encoding.UTF8.GetString(buffer, 0, end >= 0 ? end : length).Trim();
        return command.Length > 0 ? command : null;
    }

    private void Raise(string command)
    {
        try
        {
            CommandReceived?.Invoke(command);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[SingleInstance] A command handler failed: {exception}");
        }
    }

    private static async Task<bool> Delay(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static string CurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User?.Value is { } sid) return sid;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[SingleInstance] Reading the user SID failed: {exception.Message}");
        }
        return Environment.UserName; // still per user, and a valid kernel object name (no backslash)
    }

    private static int SessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }
}
