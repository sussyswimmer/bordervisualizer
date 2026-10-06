using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>A page of the settings window. Called on the UI thread by <see cref="SettingsWindow"/>.</summary>
internal interface ISettingsPage
{
    /// <summary>Shows the settings, and whatever else the page displays, as they are now.</summary>
    /// <param name="settings">The current settings.</param>
    void Refresh(Settings settings);

    /// <summary>
    /// True while the page is on screen: the window is shown and not minimized, and the page is selected. Pages run
    /// timers (preview, level meter) and listen for outside changes only while active.
    /// </summary>
    /// <param name="active">Whether the page is on screen.</param>
    void SetActive(bool active);
}

/// <summary>
/// What every page shares: applies the user's edits as live settings changes (doc 06 §3: no Save button, saved
/// 500 ms later by <see cref="SettingsService"/>), but not the control changes a page makes itself while it shows the
/// settings.
/// </summary>
internal sealed class PageEdits(AppController app)
{
    private bool refreshing;
    private bool ready; // controls fire change events while they are built (a slider's minimum moves its value)

    /// <summary>The current settings.</summary>
    public Settings Current => app.SettingsService.Current;

    /// <summary>
    /// Runs <paramref name="refresh"/>, during which control events don't count as edits. Edits count only after the
    /// first refresh, once the controls show the settings.
    /// </summary>
    public void Refresh(Action refresh)
    {
        refreshing = true;
        try
        {
            refresh();
            ready = true;
        }
        finally
        {
            refreshing = false;
        }
    }

    /// <summary>Applies an edit, unless it comes from a refresh. A failing change is logged, never thrown at WPF.</summary>
    public void Update(Func<Settings, Settings> change)
    {
        if (refreshing || !ready) return;
        try
        {
            app.SettingsService.Update(change);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Applying a change failed: {exception}");
        }
    }

    /// <summary>Sets a slider without jitter: values within <paramref name="tolerance"/> are left as they are.</summary>
    public static void Set(Slider slider, double value, double tolerance = 1e-4)
    {
        double clamped = Math.Clamp(double.IsFinite(value) ? value : slider.Minimum, slider.Minimum, slider.Maximum);
        if (Math.Abs(slider.Value - clamped) > tolerance) slider.Value = clamped;
    }

    /// <summary>
    /// Sets an inline message (a warning, the meter's state), hides it when empty, and has screen readers announce a
    /// change (the element must have <c>AutomationProperties.LiveSetting</c>).
    /// </summary>
    public static void SetMessage(TextBlock block, string text)
    {
        bool changed = block.Text != text;
        block.Text = text;
        block.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!changed || text.Length == 0 || AutomationProperties.GetLiveSetting(block) == AutomationLiveSetting.Off) return;
        try
        {
            UIElementAutomationPeer.CreatePeerForElement(block)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Announcing a message failed: {exception.Message}");
        }
    }
}
