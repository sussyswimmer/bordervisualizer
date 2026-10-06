using System.Diagnostics;
using Rimlight.Core;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Rimlight.Platform.Media;

/// <summary>
/// What's playing, read from Windows' media sessions (GSMTC: Spotify, browsers, most players; doc 05 §1), and the album
/// colors extracted from its cover art. Everything is read locally; nothing leaves the device, and Windows asks no
/// permission for it.
/// </summary>
/// <remarks>
/// <para>Everything runs on the thread pool. Session events only schedule a refresh, and one refresh runs at a time,
/// 250 ms after the last event (a track change fires two or three). A refresh picks the session to show (the playing
/// one; if several play, Windows' current one) and reads its title and artist. Its thumbnail is read for a new track
/// and again whenever the properties change (the cover can arrive after the title), not on play/pause: decoded to at
/// most 64×64, and a picture not shown yet goes to the extractor. Without a usable cover (no thumbnail, or one the
/// extractor calls "no art", a generic app icon) the previous colors stay and the track is read once more after
/// 750 ms; if it still has none, <see cref="AlbumPalette"/> becomes null. Without events a refresh still runs every
/// 30 s, so a session manager whose process went away (an Explorer restart) is noticed; it is then requested again
/// every 5 s.</para>
/// <para>Results are immutable and published by reference, so any thread may read <see cref="Current"/>,
/// <see cref="AlbumPalette"/> and <see cref="Art"/>. The change events are raised on a thread-pool thread: handlers
/// must return quickly and must not touch UI objects there.</para>
/// </remarks>
public sealed class NowPlayingService : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);        // doc 05 §1
    private static readonly TimeSpan MaxDebounce = TimeSpan.FromSeconds(1);            // a session that never stops firing
    private static readonly TimeSpan ThumbnailRetry = TimeSpan.FromMilliseconds(750);  // doc 05 §1: a late thumbnail
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);            // a hung media app can't stall us
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);         // after the media service went away
    private static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(30);             // a dead manager raises no events
    private const long ErrorLogIntervalMs = 5000;

    private readonly IPaletteExtractor extractor;
    private readonly RefreshScheduler scheduler = new(Debounce, MaxDebounce, IdleCheck);
    private readonly CancellationTokenSource stop = new(); // never disposed: late WinRT event handlers still read it
    // One delegate instance each, so every -= removes exactly what += added.
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, CurrentSessionChangedEventArgs> onCurrentSessionChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSessionManager, SessionsChangedEventArgs> onSessionsChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> onMediaPropertiesChanged;
    private readonly TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> onPlaybackInfoChanged;
    private Task? loop;
    private bool disposed;

    // Published: written by the refresh loop, read by any thread.
    private NowPlaying? current;
    private Palette? albumPalette;
    private AlbumArt? art;
    private volatile bool unavailable;
    private int propertiesDirty; // 1: properties changed since the last refresh began (set on WinRT event threads)

    // Owned by the refresh loop (one refresh at a time).
    private GlobalSystemMediaTransportControlsSessionManager? manager;
    private readonly List<GlobalSystemMediaTransportControlsSession> subscribed = [];
    private string? artTrackId;   // the track the art state below belongs to
    private bool artSettled;      // its thumbnail was read, no retry pending: read again when its properties change
    private long artMissingSince; // Stopwatch timestamp when it first had no usable cover; 0 = it has one
    private long lastErrorLogMs = long.MinValue / 2;

    /// <summary>Creates the service. Call <see cref="Start"/> to begin reading media sessions.</summary>
    /// <param name="extractor">Turns decoded cover art into a palette (Core).</param>
    public NowPlayingService(IPaletteExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(extractor);
        this.extractor = extractor;
        onCurrentSessionChanged = (_, _) => OnPropertiesEvent();
        onSessionsChanged = (_, _) => OnPropertiesEvent();
        onMediaPropertiesChanged = (_, _) => OnPropertiesEvent();
        onPlaybackInfoChanged = (_, _) => OnSessionEvent();
    }

    /// <summary>The track to show (title, artist, app, playing or not), or null while no media session exists.</summary>
    public NowPlaying? Current => Volatile.Read(ref current);

    /// <summary>
    /// The colors of the current track's cover art, or null when there is no session, no art (no thumbnail, or a
    /// generic app icon) or media sessions are unavailable: the glow then uses the manual colors. After a track change
    /// the previous track's palette stays until the new one is known. Read by the render thread every frame.
    /// </summary>
    public Palette? AlbumPalette => Volatile.Read(ref albumPalette);

    /// <summary>
    /// The current cover art, decoded small, or null when there is none. Its <see cref="AlbumArt.TrackId"/> may name
    /// the previous track for up to a second after a track change, while the new art is read.
    /// </summary>
    public AlbumArt? Art => Volatile.Read(ref art);

    /// <summary>False when Windows' media sessions can't be used (very old Windows, or a policy): manual colors only.</summary>
    public bool IsAvailable => !unavailable;

    /// <summary>Raised on a thread-pool thread after <see cref="Current"/> changed.</summary>
    public event Action? NowPlayingChanged;

    /// <summary>Raised on a thread-pool thread after <see cref="AlbumPalette"/> or <see cref="Art"/> changed.</summary>
    public event Action? AlbumArtChanged;

    /// <summary>Starts reading media sessions on the thread pool. Failures are handled and logged there; this never throws for them.</summary>
    public void Start()
    {
        if (disposed || loop is not null) return;
        CancellationToken token = stop.Token;
        loop = Task.Run(() => RunAsync(token));
    }

    /// <summary>
    /// Reads the media sessions again shortly. Call it after Explorer restarted (the taskbar's TaskbarCreated): a
    /// session manager that went away with it is then noticed at once instead of at the next 30 s check. Any thread.
    /// </summary>
    public void Refresh() => OnSessionEvent();

    /// <summary>Stops reading and unsubscribes from every session, waiting up to 2 s. Raises no events afterwards.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Callbacks run on the thread pool, so the loop's cleanup never runs on the caller's (UI) thread.
        _ = stop.CancelAsync();
        Task? running = loop;
        if (running is null) return;
        try
        {
            // WinRT calls in flight are abandoned at their next await, not waited for.
            if (!running.Wait(StopTimeout)) Trace.WriteLine("[NowPlaying] The media session reader did not stop within 2 s.");
        }
        catch (AggregateException exception)
        {
            Trace.WriteLine($"[NowPlaying] The media session reader failed while stopping: {exception.InnerException?.Message}");
        }
    }

    // Any session or session-list change: read again once the burst is over. Called on WinRT event threads; cheap.
    private void OnSessionEvent()
    {
        if (!stop.IsCancellationRequested) scheduler.Request();
    }

    // New properties, or another session: that refresh reads the thumbnail too. Set before the request it goes with.
    private void OnPropertiesEvent()
    {
        Volatile.Write(ref propertiesDirty, 1);
        OnSessionEvent();
    }

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            if (!await ConnectAsync(token).ConfigureAwait(false))
            {
                // Very old Windows or a policy (doc 05 §1): manual colors for the rest of the session.
                Trace.WriteLine("[NowPlaying] Media sessions are unavailable; the glow uses the manual colors.");
                unavailable = true;
                return;
            }
            scheduler.RequestBy(Stopwatch.GetTimestamp()); // the first read at once
            while (true)
            {
                await scheduler.WaitAsync(token).ConfigureAwait(false);
                // The media service went away (RefreshAsync): connect again; until then what is shown stays.
                if (manager is null && !await ConnectAsync(token).ConfigureAwait(false))
                {
                    scheduler.RequestBy(Stopwatch.GetTimestamp() + Ticks(ReconnectDelay));
                    continue;
                }
                await RefreshAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Disposed.
        }
        catch (Exception exception)
        {
            // Not a Windows failure (those are handled where they happen): the colors stay as they are, the app goes on.
            Trace.WriteLine($"[NowPlaying] The media session reader stopped: {exception}");
        }
        finally
        {
            Disconnect();
        }
    }

    // RequestAsync (doc 05 §1) and the session-list events. No timeout: at logon it can be slow, and nothing waits for it.
    private async Task<bool> ConnectAsync(CancellationToken token)
    {
        try
        {
            manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token)
                .WaitAsync(token).ConfigureAwait(false);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            LogThrottled($"[NowPlaying] RequestAsync failed: {exception.Message}");
        }
        if (manager is null) return false;

        try
        {
            manager.CurrentSessionChanged += onCurrentSessionChanged;
            manager.SessionsChanged += onSessionsChanged;
        }
        catch (Exception exception)
        {
            // Session events still work; only new or closed sessions go unnoticed until one of them fires.
            Trace.WriteLine($"[NowPlaying] Can't follow the session list: {exception.Message}");
        }
        Volatile.Write(ref propertiesDirty, 1); // a new manager: read the thumbnail again too
        return true;
    }

    private async Task RefreshAsync(CancellationToken token)
    {
        // Claimed first: properties that change from here on schedule another refresh that reads them.
        bool propertiesChanged = Interlocked.Exchange(ref propertiesDirty, 0) != 0;
        try
        {
            (GlobalSystemMediaTransportControlsSession? session, bool playing) = SyncSessions(manager!.GetSessions());
            if (session is null)
            {
                Publish(null);
                artTrackId = null;
                artSettled = false;
                artMissingSince = 0;
                PublishArt(null, null); // manual colors
                return;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties? properties = await session.TryGetMediaPropertiesAsync()
                .AsTask(token).WaitAsync(CallTimeout, token).ConfigureAwait(false);
            string app = session.SourceAppUserModelId ?? "";
            string? title = Clean(properties?.Title);
            string? artist = Clean(properties?.Artist);
            string trackId = $"{app}|{artist}|{title}"; // doc 05 §1
            Publish(new NowPlaying(title, artist, app.Length > 0 ? app : null, playing, trackId));
            await UpdateArtAsync(trackId, properties?.Thumbnail, propertiesChanged, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            if (propertiesChanged) Volatile.Write(ref propertiesDirty, 1); // not read yet: the next refresh reads them
            // The process behind the session manager ended (an Explorer restart, an update). Its events are gone with
            // it, so drop it and connect again shortly; what is shown stays until then. A session that just closed
            // fails the same way, so the manager itself is asked.
            if (IsDisconnected(exception) && !ManagerAnswers())
            {
                Trace.WriteLine($"[NowPlaying] The media session manager went away; reconnecting: {exception.Message}");
                Disconnect();
                scheduler.RequestBy(Stopwatch.GetTimestamp() + Ticks(ReconnectDelay));
                return;
            }
            // The session closed mid-call, or the app answered with an error or not at all. What is shown stays; the
            // session events that follow (a closed session changes the list) read again.
            LogThrottled($"[NowPlaying] Reading the media session failed: {exception.Message}");
        }
    }

    // After a call failed as if its server were gone: false when the session manager is gone, not just a session.
    private bool ManagerAnswers()
    {
        try
        {
            _ = manager!.GetSessions();
            return true;
        }
        catch (Exception exception)
        {
            return !IsDisconnected(exception);
        }
    }

    // Follows the session list, subscribing to each session's changes, and picks the session to show (null: none) and
    // whether it is playing.
    private (GlobalSystemMediaTransportControlsSession? Session, bool Playing) SyncSessions(
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> list)
    {
        var sessions = new List<GlobalSystemMediaTransportControlsSession>();
        foreach (GlobalSystemMediaTransportControlsSession? session in list)
            if (session is not null) sessions.Add(session);

        // Projected sessions compare by native object, so a new wrapper of a known session counts as known. Events are
        // removed through the wrapper they were added on.
        for (int i = subscribed.Count - 1; i >= 0; i--)
        {
            if (sessions.Contains(subscribed[i])) continue;
            Unsubscribe(subscribed[i]);
            subscribed.RemoveAt(i);
        }
        foreach (GlobalSystemMediaTransportControlsSession session in sessions)
            if (!subscribed.Contains(session) && Subscribe(session)) subscribed.Add(session);

        GlobalSystemMediaTransportControlsSession? windowsCurrent = null;
        try
        {
            windowsCurrent = manager!.GetCurrentSession();
        }
        catch (Exception exception)
        {
            LogThrottled($"[NowPlaying] Can't get the current session: {exception.Message}");
        }
        var playing = new bool[sessions.Count];
        int currentIndex = -1;
        for (int i = 0; i < sessions.Count; i++)
        {
            playing[i] = IsPlaying(sessions[i]);
            if (currentIndex < 0 && windowsCurrent is not null && sessions[i] == windowsCurrent) currentIndex = i;
        }
        int chosen = ChooseSession(playing, currentIndex);
        return chosen >= 0 ? (sessions[chosen], playing[chosen]) : (null, false);
    }

    // Doc 05 §1: browser tabs can switch the current session rapidly, so prefer the session that is playing; if
    // several play, Windows' current one. With none playing, Windows' current session (else the first) still names the
    // paused track. `current` is the index of Windows' current session, or -1. Returns -1 when there is no session.
    internal static int ChooseSession(ReadOnlySpan<bool> playing, int current)
    {
        int first = -1, count = 0;
        for (int i = 0; i < playing.Length; i++)
        {
            if (!playing[i]) continue;
            count++;
            if (first < 0) first = i;
        }
        bool currentValid = current >= 0 && current < playing.Length;
        if (count > 1 && currentValid && playing[current]) return current;
        if (count > 0) return first;
        if (currentValid) return current;
        return playing.Length > 0 ? 0 : -1;
    }

    // Reads the cover of a new track, and again whenever its properties change: the cover often arrives after the
    // title, and at first the session may still hold the previous track's cover or a generic icon (doc 05 §1). A
    // play/pause alone reads nothing, unless a missing cover is being retried.
    private async Task UpdateArtAsync(string trackId, IRandomAccessStreamReference? thumbnail, bool propertiesChanged,
        CancellationToken token)
    {
        if (!string.Equals(trackId, artTrackId, StringComparison.Ordinal))
        {
            artTrackId = trackId;
            artSettled = false;
            artMissingSince = 0;
        }
        else if (propertiesChanged)
        {
            artSettled = false;
        }
        if (artSettled) return;

        AlbumArt? decoded = null;
        if (thumbnail is not null)
        {
            try
            {
                decoded = await AlbumArtDecoder.DecodeAsync(thumbnail, trackId, CallTimeout, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (!token.IsCancellationRequested)
            {
                LogThrottled($"[NowPlaying] The thumbnail could not be decoded: {exception.Message}");
            }
        }

        // The picture already shown for this track (an event that changed something else): its colors stand.
        if (decoded is not null && AlbumArt.SameImage(Volatile.Read(ref art), decoded))
        {
            artSettled = true;
            artMissingSince = 0;
            return;
        }

        Palette? palette = decoded is null ? null : Extract(decoded);
        if (palette is null)
        {
            // No usable cover: no thumbnail, one that won't decode, or a generic app icon (the extractor's "no art").
            // The cover often arrives late (doc 05 §1): keep the previous colors and read once more 750 ms after first
            // finding none. After that the track has no art, and the manual colors show.
            long now = Stopwatch.GetTimestamp();
            if (artMissingSince == 0) artMissingSince = now;
            long retryAt = artMissingSince + Ticks(ThumbnailRetry);
            if (now < retryAt)
            {
                scheduler.RequestBy(retryAt);
                return;
            }
            PublishArt(null, decoded); // the icon, if any, is still the track's art
            artSettled = true;
            return;
        }

        PublishArt(palette, decoded);
        artSettled = true;
        artMissingSince = 0;
    }

    // On the thread pool, one call at a time (H-004).
    private Palette? Extract(AlbumArt image)
    {
        try
        {
            return extractor.Extract(image.Pixels.Span, image.Width, image.Height, image.TrackId);
        }
        catch (Exception exception)
        {
            LogThrottled($"[NowPlaying] Palette extraction failed: {exception.Message}");
            return null;
        }
    }

    private void Publish(NowPlaying? next)
    {
        if (Equals(Volatile.Read(ref current), next)) return;
        Volatile.Write(ref current, next);
        Raise(NowPlayingChanged);
    }

    private void PublishArt(Palette? palette, AlbumArt? image)
    {
        bool paletteChanged = !Equals(Volatile.Read(ref albumPalette), palette);
        bool artChanged = !AlbumArt.SameImage(Volatile.Read(ref art), image);
        if (!paletteChanged && !artChanged) return;
        if (paletteChanged) Volatile.Write(ref albumPalette, palette);
        if (artChanged) Volatile.Write(ref art, image);
        Raise(AlbumArtChanged);
    }

    private void Raise(Action? handler)
    {
        if (stop.IsCancellationRequested) return; // nothing after Dispose
        try
        {
            handler?.Invoke();
        }
        catch (Exception exception)
        {
            Trace.WriteLine($"[NowPlaying] A change handler failed: {exception}");
        }
    }

    private bool Subscribe(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            session.MediaPropertiesChanged += onMediaPropertiesChanged;
            session.PlaybackInfoChanged += onPlaybackInfoChanged;
            return true;
        }
        catch (Exception exception)
        {
            // Usually a session closing right now; the list change that follows drops it.
            Unsubscribe(session);
            LogThrottled($"[NowPlaying] Can't follow a media session: {exception.Message}");
            return false;
        }
    }

    // Removing a handler that was never added does nothing; a session that is already gone may throw.
    private void Unsubscribe(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            session.MediaPropertiesChanged -= onMediaPropertiesChanged;
        }
        catch (Exception)
        {
            // Gone: nothing left to remove.
        }
        try
        {
            session.PlaybackInfoChanged -= onPlaybackInfoChanged;
        }
        catch (Exception)
        {
            // Gone: nothing left to remove.
        }
    }

    // Drops the manager and every subscription: when the loop ends, or when the manager's process went away.
    private void Disconnect()
    {
        foreach (GlobalSystemMediaTransportControlsSession session in subscribed) Unsubscribe(session);
        subscribed.Clear();
        if (manager is null) return;
        try
        {
            manager.CurrentSessionChanged -= onCurrentSessionChanged;
        }
        catch (Exception)
        {
            // Gone: nothing left to remove.
        }
        try
        {
            manager.SessionsChanged -= onSessionsChanged;
        }
        catch (Exception)
        {
            // Gone: nothing left to remove.
        }
        manager = null;
    }

    private static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch (Exception)
        {
            return false; // closing under us
        }
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static long Ticks(TimeSpan span) => (long)(span.TotalSeconds * Stopwatch.Frequency);

    // The other end of a COM call is gone: RPC_E_SERVER_DIED(_DNE), RPC_E_DISCONNECTED, CO_E_OBJNOTCONNECTED,
    // RPC_S_SERVER_UNAVAILABLE, RPC_S_CALL_FAILED(_DNE).
    private static bool IsDisconnected(Exception exception) => exception.HResult is unchecked((int)0x80010007)
        or unchecked((int)0x80010012) or unchecked((int)0x80010108) or unchecked((int)0x800401FD)
        or unchecked((int)0x800706BA) or unchecked((int)0x800706BE) or unchecked((int)0x800706BF);

    // Track text never goes to the log (doc 01 privacy): only failures, at most every 5 s.
    private void LogThrottled(string message)
    {
        long now = Environment.TickCount64;
        if (now - lastErrorLogMs < ErrorLogIntervalMs) return;
        lastErrorLogMs = now;
        Trace.WriteLine(message);
    }
}
