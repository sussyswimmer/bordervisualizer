using System.Diagnostics;
using System.Windows.Input;

namespace Rimlight.App;

/// <summary>
/// Opens the settings window (doc 06 §3). Called on the UI thread by the tray (double-click, "Settings…"), by a second
/// start of the app ("show-settings"), by a normal start without <c>--background</c> and by the welcome window.
/// K7 implements it with the real window; until then <see cref="AppController"/> shows the welcome window instead.
/// </summary>
internal interface IShowSettings
{
    /// <summary>Shows the window, or brings it to the front if it is open.</summary>
    void ShowSettings();
}

/// <summary>
/// Checks for updates when asked ("Check for updates" in the tray, doc 06 §5). Called on the UI thread. K9 implements
/// it with Velopack; until then <see cref="AppController"/> only tells which version runs.
/// </summary>
internal interface IUpdateCheck
{
    /// <summary>Starts a check and reports the outcome to the user.</summary>
    void CheckNow();
}

/// <summary>A command for the tray icon's click and double-click. A failure is logged, never thrown at the caller.</summary>
internal sealed class RelayCommand(Action execute) : ICommand
{
    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => true;

    /// <inheritdoc />
    public void Execute(object? parameter)
    {
        try
        {
            execute();
        }
        catch (Exception exception)
        {
            // H.NotifyIcon runs a click from a timer thread through Dispatcher.Invoke, which would rethrow there.
            Trace.WriteLine($"[Tray] A click action failed: {exception}");
        }
    }
}
