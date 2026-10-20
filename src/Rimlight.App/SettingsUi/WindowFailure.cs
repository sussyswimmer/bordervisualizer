using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Rimlight.App.Logging;
using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Tells the user that a window (Settings, one of its pages, the visualizer) couldn't be built or shown, instead of
/// failing silently: what went wrong and where the log is, with an offer to open the logs folder. A plain message box,
/// which needs none of WPF-UI's styles (they may be what failed). The caller logs the full exception. UI thread only.
/// </summary>
internal static class WindowFailure
{
    private const int MaxProblemLength = 600; // a long chain of XAML errors would make the box taller than the screen

    private static bool showing;

    /// <summary>
    /// For the WPF smoke tests (Rimlight.App.Tests): they check the log lines, and a modal box would wait for a click
    /// that never comes.
    /// </summary>
    internal static bool Quiet { get; set; }

    /// <summary>The log file, as Windows names its folder: <c>%APPDATA%\Rimlight\logs\rimlight.log</c>.</summary>
    public static string LogFile => $@"%APPDATA%\{AppInfo.Name}\logs\{AppInfo.Name.ToLowerInvariant()}.log";

    /// <summary>
    /// Shows the message once the current event is done (a tray click arrives inside the icon's window procedure), one
    /// box at a time: a second failure while one is open isn't stacked on top of it.
    /// </summary>
    /// <param name="what">What couldn't open, e.g. "its settings window".</param>
    /// <param name="problem">What went wrong, one or two sentences (<see cref="Describe"/>).</param>
    /// <param name="area">The log area whose lines have the details: "Settings" or "Visualizer".</param>
    public static void Show(string what, string problem, string area = "Settings")
    {
        if (Quiet || showing || Application.Current?.Dispatcher is not { } dispatcher) return;
        showing = true;
        dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            try
            {
                MessageBoxResult answer = MessageBox.Show(Message(what, problem, area), AppInfo.Name,
                    MessageBoxButton.YesNo, MessageBoxImage.Error, MessageBoxResult.Yes);
                if (answer == MessageBoxResult.Yes) AppLog.OpenFolder();
            }
            catch (Exception exception)
            {
                Trace.WriteLine($"[App] Showing the error message failed: {exception.Message}");
            }
            finally
            {
                showing = false;
            }
        });
    }

    /// <summary>
    /// The message box's text: what couldn't open, why, and where the log is. The folder is written as Windows names it
    /// (<c>%APPDATA%</c>), so a screenshot of the box names no account, as the log itself doesn't.
    /// </summary>
    /// <param name="what">What couldn't open.</param>
    /// <param name="problem">What went wrong.</param>
    /// <param name="area">The log area whose lines have the details.</param>
    public static string Message(string what, string problem, string area = "Settings") =>
        $"{AppInfo.Name} couldn't open {what}.\n\n"
        + $"What went wrong: {problem}\n\n"
        + $"The details are in the log, {LogFile}, in the lines that start with [{area}] or [UiTheme]. "
        + "Attaching that file to a bug report helps fix this.\n\n"
        + "The glow and the tray icon keep working. Open the logs folder now?";

    /// <summary>
    /// An exception in a sentence or two: its type and message and, when it wraps others (a XAML error wraps the
    /// exception that caused it, and names the line), the innermost one's too.
    /// </summary>
    /// <param name="exception">What was thrown.</param>
    public static string Describe(Exception exception)
    {
        Exception root = exception;
        while (root.InnerException is { } inner) root = inner;
        string text = $"{exception.GetType().Name}: {exception.Message}";
        if (!ReferenceEquals(root, exception)) text += $" Caused by {root.GetType().Name}: {root.Message}";
        return text.Length <= MaxProblemLength ? text : text[..(MaxProblemLength - 1)] + "…";
    }
}
