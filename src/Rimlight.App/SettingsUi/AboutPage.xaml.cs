using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rimlight.App.Logging;
using Rimlight.App.Updates;
using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// About (doc 06 §3.6): the version, Check for updates with where updates stand and Restart now once one is downloaded
/// (doc 06 §5), the source, the license and the third-party notices, and the privacy promise.
/// </summary>
internal partial class AboutPage : UserControl, ISettingsPage
{
    private const string RepositoryUrl = UpdateBootstrap.RepositoryUrl;

    // The same promise as the README (doc 01 "Privacy", doc 08).
    private static readonly string[] Privacy =
    [
        "Only system output audio is captured, via WASAPI loopback. The microphone is never opened.",
        "Audio is analyzed in memory and immediately discarded. Nothing is recorded or saved.",
        "Track info and artwork are read locally from Windows' media controls and never leave the device.",
        "No telemetry or analytics. The only network call is the GitHub update check.",
    ];

    private readonly AppController app;
    private UpdatePhase? shownPhase;

    public AboutPage(AppController app)
    {
        this.app = app;
        InitializeComponent();
        NameText.Text = AppInfo.Name;
        VersionText.Text = $"Version {AppLog.Version}";
        TaglineText.Text = "Your screen, lit by your music.";
        LicenseText.Text = $"{AppInfo.Name} is free to use, share and change under the MIT License.";
        SourceLink.NavigateUri = RepositoryUrl;
        LicenseLink.NavigateUri = RepositoryUrl + "/blob/main/LICENSE";
        // The libraries Rimlight ships and their licenses; installed copies also have the file next to Rimlight.exe.
        NoticesLink.NavigateUri = RepositoryUrl + "/blob/main/THIRD-PARTY-NOTICES.txt";
        PrivacyList.ItemsSource = Privacy;
        AppIcon.Source = LoadIcon(96);
    }

    public void Refresh(Settings settings)
    {
    }

    public void SetActive(bool active)
    {
        if (app.Updates is not { } updates) return;
        if (active)
        {
            shownPhase = null;
            updates.StatusChanged += ShowUpdateStatus;
            ShowUpdateStatus();
        }
        else
        {
            updates.StatusChanged -= ShowUpdateStatus;
        }
    }

    /// <summary>The app's icon at the size closest to <paramref name="pixels"/>, or null if it can't be read.</summary>
    internal static ImageSource? LoadIcon(int pixels)
    {
        try
        {
            var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/Assets/Rimlight.ico", UriKind.Absolute),
                BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            BitmapFrame? best = null;
            foreach (BitmapFrame frame in decoder.Frames)
            {
                if (best is null || Math.Abs(frame.PixelWidth - pixels) < Math.Abs(best.PixelWidth - pixels)) best = frame;
            }
            best?.Freeze();
            return best;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Loading the app icon failed: {exception.Message}");
            return null;
        }
    }

    // The outcome shows below the buttons, so this check sends no notification (only "Update ready" does).
    private void OnCheckForUpdates(object sender, RoutedEventArgs e) => app.CheckForUpdates(fromSettings: true);

    private void OnRestart(object sender, RoutedEventArgs e) => app.RestartToUpdate();

    private void ShowUpdateStatus()
    {
        UpdateStatus status = app.Updates?.Status ?? new UpdateStatus(UpdatePhase.Unavailable);
        bool ready = status.Phase == UpdatePhase.Ready;
        bool focused = UpdateButton.IsKeyboardFocusWithin;
        // Stays enabled while a check runs (a second click just waits for it), so the keyboard focus isn't lost.
        UpdateButton.IsEnabled = status.CanCheck;
        UpdateButton.Visibility = ready ? Visibility.Collapsed : Visibility.Visible;
        RestartButton.Visibility = ready ? Visibility.Visible : Visibility.Collapsed;
        if (ready && focused) RestartButton.Focus(); // the check the user started ends in this button
        string text = status.Describe(AppInfo.Name);
        // Screen readers hear each new phase, not every 2 % of a download.
        if (status.Phase == UpdatePhase.Downloading && shownPhase == UpdatePhase.Downloading) UpdateStatusText.Text = text;
        else PageEdits.SetMessage(UpdateStatusText, text);
        shownPhase = status.Phase;
    }

    private void OnOpenLogs(object sender, RoutedEventArgs e) => app.OpenLogsFolder();
}
