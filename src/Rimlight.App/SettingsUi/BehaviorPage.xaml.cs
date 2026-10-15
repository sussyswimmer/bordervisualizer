using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Rimlight.Core;
using Rimlight.Platform.Shell;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Behavior (doc 06 §3.5): pause in fullscreen apps, on battery, hide from screen capture, start with Windows, the
/// glow shortcut with a recorder and an inline warning when Windows won't take it (doc 06 §4), and automatic updates.
/// </summary>
internal partial class BehaviorPage : UserControl, ISettingsPage
{
    private const int F12 = 0x7B;

    private readonly AppController app;
    private readonly PageEdits edits;
    private bool recording;

    public BehaviorPage(AppController app)
    {
        this.app = app;
        edits = new PageEdits(app);
        InitializeComponent();
        // Also keys something above has marked handled (the page frame takes F5 for itself).
        RecordButton.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(OnRecorderKeyDown), handledEventsToo: true);
        RecordButton.LostKeyboardFocus += (_, _) => StopRecording();
        ShowRecorder();
    }

    public void Refresh(Settings settings) => edits.Refresh(() =>
    {
        FullscreenSwitch.IsChecked = settings.PauseInFullscreen;
        BatteryBox.SelectedIndex = settings.OnBattery switch
        {
            BatteryBehavior.Normal => 0,
            BatteryBehavior.Pause => 2,
            _ => 1,
        };
        CaptureSwitch.IsChecked = settings.HideFromScreenCapture;
        StartupSwitch.IsChecked = settings.LaunchAtStartup;
        PageEdits.SetMessage(StartupWarning, app.StartupRegistered ? ""
            : settings.LaunchAtStartup ? "Windows didn't accept the startup entry, so the app won't start by itself. The log has details."
            : "Windows didn't let the app remove its startup entry, so it may still start when you sign in. The log has details.");
        UpdateSwitch.IsChecked = settings.AutoUpdate;
        ShowHotkeyStatus();
    });

    public void SetActive(bool active)
    {
        if (!active) StopRecording();
    }

    /// <summary>Shows the shortcut and whether it works (also when another part of the app changed it).</summary>
    public void ShowHotkeyStatus()
    {
        HotkeyStatus status = app.Hotkey;
        string? gesture = status.Gesture?.ToString();
        HotkeyText.Text = status.State switch
        {
            HotkeyState.None => "None. Pick one to turn the glow on and off from anywhere.",
            HotkeyState.Invalid => $"\"{status.Text}\"",
            _ => $"Press {gesture} anywhere to turn the glow on or off.",
        };
        ClearButton.IsEnabled = status.State != HotkeyState.None || status.Text.Length > 0;
        if (!recording)
        {
            PageEdits.SetMessage(HotkeyWarning, status.State switch
            {
                HotkeyState.InUse => $"Another app already uses {gesture}, so it doesn't work here. Pick a different one.",
                HotkeyState.Invalid => $"\"{status.Text}\" isn't a shortcut Windows can use. Pick a different one.",
                HotkeyState.Failed => $"Windows didn't accept {gesture} as a shortcut. Pick a different one.",
                _ => "",
            });
        }
    }

    private void OnFullscreenChanged(object sender, RoutedEventArgs e) =>
        edits.Update(s => s with { PauseInFullscreen = FullscreenSwitch.IsChecked == true });

    private void OnBatteryChanged(object sender, SelectionChangedEventArgs e)
    {
        BatteryBehavior? behavior = BatteryBox.SelectedIndex switch
        {
            0 => BatteryBehavior.Normal,
            1 => BatteryBehavior.Reduce,
            2 => BatteryBehavior.Pause,
            _ => null,
        };
        if (behavior is { } chosen) edits.Update(s => s with { OnBattery = chosen });
    }

    private void OnCaptureChanged(object sender, RoutedEventArgs e) =>
        edits.Update(s => s with { HideFromScreenCapture = CaptureSwitch.IsChecked == true });

    // The registry entry follows the setting (AppController, before this page refreshes); a refusal shows below.
    private void OnStartupChanged(object sender, RoutedEventArgs e) =>
        edits.Update(s => s with { LaunchAtStartup = StartupSwitch.IsChecked == true });

    private void OnUpdateChanged(object sender, RoutedEventArgs e) =>
        edits.Update(s => s with { AutoUpdate = UpdateSwitch.IsChecked == true });

    private void OnRecordClick(object sender, RoutedEventArgs e)
    {
        if (recording) StopRecording();
        else StartRecording();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        StopRecording();
        edits.Update(s => s with { ToggleHotkey = "" }); // H-009: empty means no shortcut
    }

    // While recording, the current shortcut is released, so pressing it again shows up here instead of toggling the
    // glow. Esc cancels; Tab leaves.
    private void StartRecording()
    {
        recording = true;
        app.SuspendHotkey();
        ShowRecorder();
        PageEdits.SetMessage(HotkeyWarning, "");
        RecordButton.Focus();
    }

    private void StopRecording()
    {
        if (!recording) return;
        recording = false;
        app.ResumeHotkey();
        ShowRecorder();
        ShowHotkeyStatus();
    }

    private void ShowRecorder()
    {
        RecordButton.Content = recording ? "Press a shortcut…" : "Change";
        System.Windows.Automation.AutomationProperties.SetHelpText(RecordButton, recording
            ? "Press the new shortcut, for example Ctrl+Alt+L. Escape cancels."
            : "Then press the new shortcut.");
    }

    private void OnRecorderKeyDown(object sender, KeyEventArgs e)
    {
        if (!recording) return;
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,               // with Alt held
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        HotkeyModifiers modifiers = Modifiers(Keyboard.Modifiers);
        // Tab and Shift+Tab leave the recorder (it stops when it loses focus), and Alt+F4 still closes the window.
        if ((modifiers & ~HotkeyModifiers.Shift) == HotkeyModifiers.None && key == Key.Tab) return;
        if (modifiers == HotkeyModifiers.Alt && key == Key.F4)
        {
            StopRecording();
            return;
        }
        e.Handled = true;

        if (modifiers == HotkeyModifiers.None && key == Key.Escape)
        {
            StopRecording();
            return;
        }
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            // Still choosing: show the modifiers held so far.
            RecordButton.Content = modifiers == HotkeyModifiers.None ? "Press a shortcut…" : $"{Describe(modifiers)}…";
            return;
        }

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (HotkeyGesture.TryCreate(modifiers, virtualKey, out HotkeyGesture gesture))
        {
            string text = gesture.ToString();
            recording = false; // the new shortcut is registered by the settings change, not by resuming the old one
            edits.Update(s => s with { ToggleHotkey = text });
            app.ResumeHotkey(); // a no-op once the change registered it; restores the old one if nothing changed
            ShowRecorder();
            ShowHotkeyStatus();
            return;
        }
        bool needsModifier = (modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0;
        PageEdits.SetMessage(HotkeyWarning, HotkeyGesture.KeyName(virtualKey) is null
            ? "That key can't be part of a shortcut. Try a letter, a number or a function key."
            : virtualKey == F12 && modifiers == HotkeyModifiers.None ? "Windows keeps F12 for itself. Add Ctrl, Alt or Win."
            : needsModifier ? "Add Ctrl, Alt or Win to that key, for example Ctrl+Alt+L."
            : "Windows uses that combination itself. Pick a different one.");
        RecordButton.Content = "Press a shortcut…";
    }

    private static HotkeyModifiers Modifiers(ModifierKeys keys)
    {
        var modifiers = HotkeyModifiers.None;
        if (keys.HasFlag(ModifierKeys.Control)) modifiers |= HotkeyModifiers.Ctrl;
        if (keys.HasFlag(ModifierKeys.Alt)) modifiers |= HotkeyModifiers.Alt;
        if (keys.HasFlag(ModifierKeys.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (keys.HasFlag(ModifierKeys.Windows)) modifiers |= HotkeyModifiers.Win;
        return modifiers;
    }

    private static string Describe(HotkeyModifiers modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        return string.Join("+", parts) + "+";
    }
}
