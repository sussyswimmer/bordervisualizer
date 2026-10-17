using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Rimlight.App.SettingsUi;

namespace Rimlight.App.Tests;

/// <summary>
/// The test process's UI thread: one STA thread that owns the WPF application, as the app's main thread does. WPF
/// allows one <see cref="System.Windows.Application"/> per process and ties it and every window to the thread that
/// made them, so every test runs its WPF code here through <see cref="Run{T}"/>, one test at a time.
/// </summary>
/// <remarks>
/// The thread sets up what <c>Program.Main</c> and <c>AppController.Start</c> do before any window: an application
/// object that only an explicit shutdown ends (App.xaml's <c>ShutdownMode</c>: closing the last window must not end
/// it), then WPF-UI's theme, loaded by <see cref="UiTheme.Load"/>. The application is a plain
/// <see cref="Application"/>, not the app's <see cref="App"/>: WPF's constructor queues <c>OnStartup</c> on the
/// dispatcher, and App's would claim the user's single instance (handing a running Rimlight a request), open the log
/// and start the tray icon, the capture and the overlay. App.xaml holds nothing else. Exceptions that reach the
/// dispatcher are recorded and survived, as the app logs and survives them (<c>AppLog</c>), and
/// <see cref="TakeUnhandled"/> hands them to the test.
/// </remarks>
internal static class UiThread
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);
    private static readonly ConcurrentQueue<Exception> Unhandled = new();
    private static readonly Lazy<Dispatcher> UiDispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Runs <paramref name="action"/> on the UI thread and waits for it; what it throws is thrown here.</summary>
    public static void Run(Action action) => Run<object?>(() =>
    {
        action();
        return null;
    });

    /// <summary>
    /// Runs <paramref name="function"/> on the UI thread and returns its result; what it throws is thrown here.
    /// </summary>
    /// <param name="function">The code to run.</param>
    /// <param name="priority">A lower one than the default lets everything queued above it run first (layout, Loaded,
    /// a frame's navigation).</param>
    public static T Run<T>(Func<T> function, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        Dispatcher dispatcher = UiDispatcher.Value;
        T result = default!;
        ExceptionDispatchInfo? failure = null;
        DispatcherOperation operation = dispatcher.BeginInvoke(priority, () =>
        {
            try
            {
                result = function();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        // A wait that never ends (a modal box, a deadlock) fails the test instead of hanging the run.
        if (operation.Wait(OperationTimeout) != DispatcherOperationStatus.Completed)
            throw new TimeoutException($"The UI thread didn't run the code within {OperationTimeout.TotalSeconds} s.");
        failure?.Throw();
        return result;
    }

    /// <summary>
    /// Lets the UI thread work (layout, Loaded, navigation, timers) until <paramref name="condition"/> holds there, or
    /// fails after 15 s.
    /// </summary>
    /// <param name="condition">Checked on the UI thread, once everything queued above idle priority has run.</param>
    /// <param name="what">What is awaited, for the failure message.</param>
    public static void WaitUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!Run(condition, DispatcherPriority.ApplicationIdle))
        {
            if (clock.Elapsed > WaitTimeout)
                throw new TimeoutException($"Waited {WaitTimeout.TotalSeconds} s for {what}.");
            Thread.Sleep(20);
        }
    }

    /// <summary>Lets everything queued on the UI thread above idle priority run.</summary>
    public static void Settle() => Run(() => true, DispatcherPriority.ApplicationIdle);

    /// <summary>The exceptions that reached the dispatcher since the last call; the app would have logged them.</summary>
    public static Exception[] TakeUnhandled()
    {
        var taken = new List<Exception>();
        while (Unhandled.TryDequeue(out Exception? exception)) taken.Add(exception);
        return [.. taken];
    }

    private static Dispatcher Start()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // The log lines from here on, UiTheme's too; and no modal error box, which would wait for a click.
                TraceRecorder.Install();
                WindowFailure.Quiet = true;
                Dispatcher.CurrentDispatcher.UnhandledException += (_, e) =>
                {
                    Unhandled.Enqueue(e.Exception);
                    e.Handled = true;
                };
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; // Application.Current now
                // As AppController.Start, before any window or the tray icon exists.
                UiTheme.Load();
                ready.SetResult(Dispatcher.CurrentDispatcher);
            }
            catch (Exception exception)
            {
                ready.SetException(exception);
                return;
            }
            Dispatcher.Run();
        })
        {
            IsBackground = true, // ends with the test run, whatever is still open
            Name = "UI thread (WPF smoke tests)",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }
}
