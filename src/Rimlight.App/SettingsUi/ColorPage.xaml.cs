using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Rimlight.App.Overlay;
using Rimlight.Core;
using Rimlight.Platform.Media;

namespace Rimlight.App.SettingsUi;

/// <summary>
/// Color (doc 06 §3.2): album art or manual colors, Override album color, the two manual colors, the color balance,
/// and what's playing with its cover and the two colors taken from it (K4's media service).
/// </summary>
internal partial class ColorPage : UserControl, ISettingsPage
{
    private static readonly Settings Defaults = new(); // the colors an invalid hex falls back to (MusicGlowSource)

    private readonly AppController app;
    private readonly PageEdits edits;
    private AlbumArt? thumbnailArt; // the art the thumbnail was made from
    private int mediaQueued;
    private bool active;

    public ColorPage(AppController app)
    {
        this.app = app;
        edits = new PageEdits(app);
        InitializeComponent();
        PrimaryEditor.Configure("First color", "Covers most of the edge.");
        SecondaryEditor.Configure("Second color", "Fills the rest of the edge.");
        PrimaryEditor.ColorEdited += hex => edits.Update(s => s with { PrimaryHex = hex });
        SecondaryEditor.ColorEdited += hex => edits.Update(s => s with { SecondaryHex = hex });
    }

    public void Refresh(Settings settings) => edits.Refresh(() =>
    {
        AlbumArtChoice.IsChecked = settings.ColorMode == ColorMode.AlbumArt;
        ManualChoice.IsChecked = settings.ColorMode == ColorMode.Manual;
        OverrideSwitch.IsChecked = settings.OverrideAlbumColor;
        OverrideSwitch.IsEnabled = settings.ColorMode == ColorMode.AlbumArt; // only album art has colors to override
        OverrideDescription.Text = settings.ColorMode == ColorMode.AlbumArt
            ? "Use your colors even when a song has cover art."
            : "Only matters when colors come from the album art.";
        PrimaryEditor.Show(settings.PrimaryHex, Defaults.PrimaryHex);
        SecondaryEditor.Show(settings.SecondaryHex, Defaults.SecondaryHex);
        PageEdits.Set(RatioSlider, settings.PrimaryRatio * 100);
        RatioValue.Text = Balance(settings.PrimaryRatio);
        ShowNowPlaying(settings);
    });

    public void SetActive(bool active)
    {
        if (active == this.active) return;
        this.active = active;
        if (app.Media is not { } media) return;
        // The media service raises these on the thread pool; only while the page is on screen.
        if (active)
        {
            media.NowPlayingChanged += OnMediaChanged;
            media.AlbumArtChanged += OnMediaChanged;
        }
        else
        {
            media.NowPlayingChanged -= OnMediaChanged;
            media.AlbumArtChanged -= OnMediaChanged;
        }
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        ColorMode mode = ReferenceEquals(sender, ManualChoice) ? ColorMode.Manual : ColorMode.AlbumArt;
        edits.Update(s => s with { ColorMode = mode });
    }

    private void OnOverrideChanged(object sender, RoutedEventArgs e) =>
        edits.Update(s => s with { OverrideAlbumColor = OverrideSwitch.IsChecked == true });

    private void OnRatioChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        edits.Update(s => s with { PrimaryRatio = (float)(Math.Round(e.NewValue) / 100) });

    // Thread pool: one queued refresh shows the latest state however many events arrive meanwhile.
    private void OnMediaChanged()
    {
        if (Interlocked.Exchange(ref mediaQueued, 1) != 0) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Volatile.Write(ref mediaQueued, 0);
            if (active) ShowNowPlaying(app.SettingsService.Current);
        });
    }

    private void ShowNowPlaying(Settings settings)
    {
        try
        {
            NowPlayingService? media = app.Media;
            NowPlaying? track = media?.IsAvailable == true ? media.Current : null;
            NowPlayingHeading.Text = track is { IsPlaying: false } ? "Paused" : "Now playing";
            NowPlayingText.Text = media?.IsAvailable != true ? "Not available on this PC"
                : track is null ? "Nothing is playing"
                : TrackText(track);
            NowPlayingText.ToolTip = track is null ? null : NowPlayingText.Text;

            // Stale art (from the previous track) is hidden; it is replaced within about a second.
            AlbumArt? art = media?.Art;
            if (art is null || track is null || !string.Equals(art.TrackId, track.TrackId, StringComparison.Ordinal)) art = null;
            if (!ReferenceEquals(art, thumbnailArt))
            {
                thumbnailArt = art;
                Thumbnail.Source = art is null ? null : Bitmap(art);
            }

            Palette? colors = track is null ? null : media!.AlbumPalette;
            if (colors is not null && colors.SourceTrackId is { } source && !string.Equals(source, track!.TrackId, StringComparison.Ordinal))
                colors = null;
            SwatchPanel.Visibility = colors is null ? Visibility.Collapsed : Visibility.Visible;
            if (colors is not null)
            {
                Swatch(AlbumPrimary, colors.Primary, "First album color");
                Swatch(AlbumSecondary, colors.Secondary, "Second album color");
            }
            AlbumColorsText.Text = ColorsText(settings, track, colors);
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Showing what's playing failed: {exception.Message}");
        }
    }

    private static string ColorsText(Settings settings, NowPlaying? track, Palette? colors)
    {
        if (colors is null)
            return track is null || settings.ColorMode == ColorMode.Manual ? "The glow uses your colors."
                : "No cover colors for this track, so the glow uses your colors.";
        if (MusicGlowSource.UsesAlbumColors(settings)) return "The glow uses the colors from this cover.";
        return settings.ColorMode == ColorMode.Manual
            ? "Colors from the cover. Manual is on, so the glow uses your colors."
            : "Colors from the cover. Override is on, so the glow uses your colors.";
    }

    // "Title · Artist"; the shell's own text, never logged.
    private static string TrackText(NowPlaying track)
    {
        string title = string.IsNullOrWhiteSpace(track.Title) ? "Unknown title" : track.Title.Trim();
        return string.IsNullOrWhiteSpace(track.Artist) ? title : $"{title} · {track.Artist.Trim()}";
    }

    private static void Swatch(System.Windows.Shapes.Ellipse swatch, Rgb linear, string name)
    {
        string hex = SrgbHex.Format(linear);
        SrgbColor.TryParse(hex, out SrgbColor color);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        swatch.Fill = brush;
        swatch.ToolTip = hex;
        AutomationProperties.SetName(swatch, $"{name} {hex}");
    }

    // K4's art: BGRA8 with straight alpha, rows tightly packed, at most 64 px (WPF's Bgra32).
    private static BitmapSource? Bitmap(AlbumArt art)
    {
        try
        {
            var bitmap = BitmapSource.Create(art.Width, art.Height, 96, 96, PixelFormats.Bgra32, null, art.Pixels.ToArray(), art.Width * 4);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[Settings] Showing the cover failed: {exception.Message}");
            return null;
        }
    }

    private static string Balance(float ratio)
    {
        int first = (int)Math.Round(ratio * 100);
        return string.Create(CultureInfo.CurrentCulture, $"{first} / {100 - first}");
    }
}
