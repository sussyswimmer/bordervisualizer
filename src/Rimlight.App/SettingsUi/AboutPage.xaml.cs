using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rimlight.App.Logging;
using Rimlight.Core;

namespace Rimlight.App.SettingsUi;

/// <summary>About (doc 06 §3.6): the version, Check for updates (K9's hook), the source and license, and the privacy promise.</summary>
internal partial class AboutPage : UserControl, ISettingsPage
{
    private const string RepositoryUrl = "https://github.com/sussyswimmer/bordervisualizer";

    // The same promise as the README (doc 01 "Privacy", doc 08).
    private static readonly string[] Privacy =
    [
        "Only system output audio is captured, via WASAPI loopback. The microphone is never opened.",
        "Audio is analyzed in memory and immediately discarded. Nothing is recorded or saved.",
        "Track info and artwork are read locally from Windows' media controls and never leave the device.",
        "No telemetry or analytics. The only network call is the GitHub update check.",
    ];

    private readonly AppController app;

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
        PrivacyList.ItemsSource = Privacy;
        AppIcon.Source = LoadIcon(96);
    }

    public void Refresh(Settings settings)
    {
    }

    public void SetActive(bool active)
    {
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

    private void OnCheckForUpdates(object sender, RoutedEventArgs e) => app.CheckForUpdates();

    private void OnOpenLogs(object sender, RoutedEventArgs e) => app.OpenLogsFolder();
}
