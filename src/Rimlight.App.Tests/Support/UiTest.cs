using System.IO;
using System.Windows;
using System.Windows.Media;
using Rimlight.Core;
using Xunit;

namespace Rimlight.App.Tests;

/// <summary>
/// What every smoke test shares: an <see cref="AppController"/> that has its settings (in a folder of its own, never
/// the user's) and starts nothing else (<see cref="AppController.Unstarted"/>), created on the UI thread; the windows
/// a test opened, closed afterwards; and <see cref="AssertNoFailures"/>, which fails on any exception that reached the
/// dispatcher and on any failure the app logged meanwhile. Each test calls it at its end, and the teardown calls it
/// again, so a test that failed on something else still shows what the app logged.
/// </summary>
public abstract class UiTest : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "rimlight-app-tests-" + Guid.NewGuid().ToString("N"));
    private readonly List<Window> windows = [];
    private bool disposed;

    protected UiTest()
    {
        if (!OperatingSystem.IsWindows()) return; // every test is skipped there, so nothing runs this far
        UiThread.Run(() => { }); // starts the UI thread and loads the theme; ThemeTests checks that on its own
        TraceRecorder.Clear();
        UiThread.TakeUnhandled();
        Controller = UiThread.Run(() =>
            AppController.Unstarted(new SettingsService(CoreFactory.CreateSettingsStore(directory))));
    }

    /// <summary>The app around the windows. UI thread only.</summary>
    internal AppController Controller { get; } = null!;

    public void Dispose()
    {
        if (disposed || !OperatingSystem.IsWindows()) return;
        disposed = true;
        GC.SuppressFinalize(this);
        try
        {
            UiThread.Run(() =>
            {
                // As the app quits: Settings closes for real instead of hiding (AppController.IsExiting), the pending
                // change is saved, then every window closes and the controller lets go of the rest.
                Controller.EndSession();
                foreach (Window window in windows)
                {
                    try
                    {
                        window.Close();
                    }
                    catch (InvalidOperationException)
                    {
                        // Already closed, or closing.
                    }
                }
                Controller.Dispose();
            });
        }
        finally
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A scanner holding the file a moment; the temp folder is the system's to clean.
            }
        }
        // What closing logged, and what the app logged before a test failed on something else (a window that never
        // showed): the reason is usually in these lines.
        AssertNoFailures();
    }

    /// <summary>Closes <paramref name="window"/> when the test ends. UI thread.</summary>
    internal T Track<T>(T window)
        where T : Window
    {
        windows.Add(window);
        return window;
    }

    /// <summary>
    /// Fails if an exception reached the dispatcher, or the app logged a failure, since the test started (or the last
    /// call). The message lists them all.
    /// </summary>
    internal static void AssertNoFailures()
    {
        UiThread.Settle();
        Exception[] unhandled = UiThread.TakeUnhandled();
        string[] failures = TraceRecorder.TakeFailures();
        Assert.True(unhandled.Length == 0 && failures.Length == 0,
            "Unhandled on the UI thread:\n" + string.Join("\n", unhandled.Select(e => e.ToString()))
            + "\nLogged failures:\n" + string.Join("\n", failures));
    }

    /// <summary>Puts a window far off every screen before it is shown, so a test run doesn't flash windows.</summary>
    internal static void MoveOffScreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    /// <summary>
    /// Lays <paramref name="element"/> out at <paramref name="width"/> × <paramref name="height"/> DIPs, as a window
    /// would, without a window; returns the size it asked for.
    /// </summary>
    internal static Size Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        return element.DesiredSize;
    }

    /// <summary>The element and everything under it in the visual tree.</summary>
    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            DependencyObject next = pending.Pop();
            yield return next;
            for (int i = VisualTreeHelper.GetChildrenCount(next) - 1; i >= 0; i--)
                pending.Push(VisualTreeHelper.GetChild(next, i));
        }
    }
}
