namespace Rimlight.App;

/// <summary>
/// Decides what a click on a tray notification does. The shell reports a click (<c>NIN_BALLOONUSERCLICK</c>) without
/// saying which notification it was, and a new notification replaces the one on screen, so the click action belongs
/// to the notification shown last, and only while it is on screen: between its <c>NIN_BALLOONSHOW</c> and its
/// close (<c>NIN_BALLOONTIMEOUT</c>, <c>NIN_BALLOONHIDE</c>). A click on an older notification, one being replaced
/// or one already moved to Windows' notification list does nothing. That way the "still running in the tray" toast
/// (K7) can never restart the app for an update (K9). Not thread-safe; the tray uses it on the UI thread, where the
/// shell's messages arrive in order.
/// </summary>
internal sealed class TrayNotificationClicks
{
    private Action? action; // the click action of the notification shown last
    private bool onScreen; // that notification has been shown and hasn't closed since

    /// <summary>A notification was just handed to the shell.</summary>
    /// <param name="clicked">What a click on it does, or null for nothing.</param>
    public void Showing(Action? clicked)
    {
        action = clicked;
        onScreen = false; // until the shell shows it; the one on screen is being replaced
    }

    /// <summary>The shell showed a notification: the last one handed to it.</summary>
    public void Shown() => onScreen = true;

    /// <summary>The shell closed a notification (timeout, the close button, or the icon went away).</summary>
    public void Closed()
    {
        // A close while the last notification isn't on screen yet is the replaced one's; the last one stays armed.
        if (!onScreen) return;
        onScreen = false;
        action = null;
    }

    /// <summary>The user clicked a notification.</summary>
    /// <returns>What to run: the on-screen notification's action, once; otherwise null.</returns>
    public Action? Clicked()
    {
        if (!onScreen) return null;
        Action? clicked = action;
        onScreen = false;
        action = null;
        return clicked;
    }
}
