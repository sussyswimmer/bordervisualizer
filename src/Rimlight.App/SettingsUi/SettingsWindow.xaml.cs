using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Rimlight.Core;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// The settings window (doc 06 §3): a Fluent window with Mica that follows Windows' light or dark theme, with six pages
/// in a navigation view. Every change applies live. Closing only hides it (the app keeps running in the tray), and
/// nothing of it runs while it is hidden or minimized. Created on first use and kept; use it on the UI thread only.
/// </summary>
internal partial class SettingsWindow : FluentWindow
{
    private const int WmDisplayChange = 0x007E;
    private const int WmSettingChange = 0x001A;
    private const int WmThemeChanged = 0x031A;
    private const int WmSysColorChange = 0x0015;

    private readonly AppController app;
    private readonly DisplaysPage displays;
    private readonly BehaviorPage behavior;
    private ISettingsPage? selected;
    private ISettingsPage? active;
    private IDisposable? audio;
    private bool subscribed;
    private bool themeQueued;
    private bool sized;
    private bool toastShown;

    /// <summary>Builds the window and its pages; <see cref="Present"/> shows it.</summary>
    /// <param name="app">The app, for its settings, services and actions.</param>
    public SettingsWindow(AppController app)
    {
        this.app = app;
        InitializeComponent();
        Title = $"{AppInfo.Name} Settings";
        WindowTitleBar.Title = Title;
        WindowTitleBar.Icon = TitleIcon();

        displays = new DisplaysPage(app);
        behavior = new BehaviorPage(app);
        ISettingsPage[] pages = [new AppearancePage(app), new ColorPage(app), new MotionPage(app), displays, behavior, new AboutPage(app)];
        Navigation.SetPageProviderService(new PageProvider(pages));
        Navigation.Navigated += (_, e) => OnNavigated(e.Page as ISettingsPage);
        // Windows' "Show animations in Windows" off: no slide between pages.
        if (!SystemParameters.ClientAreaAnimation) Navigation.TransitionDuration = 0;

        Loaded += OnLoaded;
        IsVisibleChanged += (_, _) => UpdateActivity();
        StateChanged += (_, _) => UpdateActivity();
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Shows the window, or brings it to the front, on its last page.</summary>
    public void Present()
    {
        UiTheme.Sync();
        ApplyTextSize();
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // Top-level windows get display and theme broadcasts, also while hidden.
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProcedure);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // WPF-UI applied the Mica backdrop; this matches it (and the title bar) to the theme the app shows now.
            WindowBackgroundManager.UpdateBackground(this, UiTheme.Current, WindowBackdropType.Mica);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Applying the window backdrop failed: {exception.Message}");
        }
        if (selected is null) Navigate(typeof(AppearancePage));
    }

    private void Navigate(Type page)
    {
        try
        {
            Navigation.Navigate(page);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Opening the {page.Name} failed: {exception}");
        }
    }

    private void OnNavigated(ISettingsPage? page)
    {
        selected = page;
        UpdateActivity();
    }

    // The selected page runs only while the window is on screen, and outside changes are followed only then.
    private void UpdateActivity()
    {
        bool shown = IsVisible && WindowState != WindowState.Minimized;
        Subscribe(shown);
        ISettingsPage? next = shown ? selected : null;
        if (!ReferenceEquals(next, active))
        {
            active?.SetActive(false);
            active = next;
            if (active is not null)
            {
                active.SetActive(true); // first: a page reads what it shows (the monitors, the media) here
                active.Refresh(app.SettingsService.Current);
            }
        }

        UpdateAudioRequest();
    }

    // The analyzer runs for the meter, and for the preview in music sync (the only mode in which it uses audio), also
    // when the glow itself doesn't listen (turned off, paused).
    private void UpdateAudioRequest()
    {
        bool wantsAudio = active is MotionPage
            || (active is AppearancePage && app.SettingsService.Current.Animation == AnimationMode.MusicSync);
        if (wantsAudio && audio is null) audio = app.RequestAudio();
        else if (!wantsAudio && audio is not null)
        {
            audio.Dispose();
            audio = null;
        }
    }

    private void Subscribe(bool on)
    {
        if (on == subscribed) return;
        subscribed = on;
        if (on)
        {
            app.SettingsService.Changed += OnSettingsChanged;
            app.HotkeyChanged += OnHotkeyChanged;
        }
        else
        {
            app.SettingsService.Changed -= OnSettingsChanged;
            app.HotkeyChanged -= OnHotkeyChanged;
        }
    }

    // A change from any page, the tray or the shortcut: the page on screen shows it.
    private void OnSettingsChanged(Settings settings)
    {
        active?.Refresh(settings);
        UpdateAudioRequest();
    }

    private void OnHotkeyChanged() => behavior.ShowHotkeyStatus();

    // Closing hides the window; the app keeps running in the tray (doc 06 §3). Only quitting (or Windows ending the
    // session) really closes it.
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (app.IsExiting) return;
        e.Cancel = true;
        Hide();
        if (toastShown || !app.IsFirstRunSession) return;
        // Once, in the first session: where the app went (the setting FirstRunComplete marks that session).
        toastShown = true;
        app.Tray?.Notify(AppInfo.Name, $"{AppInfo.Name} is still running in the tray.", userInitiated: true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        active?.SetActive(false);
        active = null;
        Subscribe(false);
        audio?.Dispose();
        audio = null;
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WmDisplayChange:
                displays.OnDisplaysChanged();
                break;
            case WmThemeChanged or WmSysColorChange:
                QueueThemeSync();
                break;
            case WmSettingChange when lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet":
                QueueThemeSync(); // "Choose your app mode"
                break;
        }
        return IntPtr.Zero;
    }

    // Outside the window procedure, once per burst of broadcasts.
    private void QueueThemeSync()
    {
        if (themeQueued) return;
        themeQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            themeQueued = false;
            UiTheme.Sync();
        });
    }

    // Windows' "Text size" (Accessibility > Text size, up to 225%): WPF doesn't follow it by itself, so the content
    // is scaled by it each time the window opens, and the first opening makes the window larger as far as the screen
    // allows (doc 06 §3: works at 200% text size).
    private void ApplyTextSize()
    {
        double factor = TextScaleFactor();
        Navigation.LayoutTransform = factor > 1.01 ? new ScaleTransform(factor, factor) : Transform.Identity;
        if (sized || IsVisible) return;
        sized = true;
        if (factor <= 1.01) return;
        Rect area = SystemParameters.WorkArea;
        Width = Math.Min(Width * Math.Min(factor, 1.6), area.Width * 0.95);
        Height = Math.Min(Height * Math.Min(factor, 1.6), area.Height * 0.95);
    }

    private static double TextScaleFactor()
    {
        try
        {
            double factor = new global::Windows.UI.ViewManagement.UISettings().TextScaleFactor;
            return double.IsFinite(factor) ? Math.Clamp(factor, 1, 2.25) : 1;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Reading the text size failed: {exception.Message}");
            return 1;
        }
    }

    // 16 DIPs, from the icon's 32-pixel image so it stays sharp up to 200% scaling.
    private static IconElement? TitleIcon() =>
        AboutPage.LoadIcon(32) is { } source ? new ImageIcon { Source = source, Width = 16, Height = 16 } : null;

    // The navigation view asks for a page by type; the window made each one once.
    private sealed class PageProvider(ISettingsPage[] pages) : INavigationViewPageProvider
    {
        public object? GetPage(Type pageType) => Array.Find(pages, page => page.GetType() == pageType);
    }
}
