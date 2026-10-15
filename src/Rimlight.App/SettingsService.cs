using System.Diagnostics;
using Rimlight.Core;

namespace Rimlight.App;

/// <summary>
/// Owns the current settings: one immutable snapshot that the UI replaces and other threads read by reference
/// (doc 02 "Settings"). Changes apply live and are saved 500 ms after the last one (doc 06 §3).
/// </summary>
internal sealed class SettingsService : IDisposable
{
    private const int SaveDelayMs = 500;

    private readonly ISettingsStore store;
    private readonly Timer saveTimer;
    private readonly object saveGate = new(); // serializes store calls (H-004); never taken per frame or by Update
    private Settings current;
    private int savePending; // 1 while a change is unsaved; set by Update without the lock, so the UI never waits on I/O
    private bool disposed;

    public SettingsService(ISettingsStore store)
    {
        this.store = store;
        current = Load(store);
        saveTimer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>The current snapshot. Safe to read from any thread.</summary>
    public Settings Current => Volatile.Read(ref current);

    /// <summary>Raised on the thread that called <see cref="Update"/> (the UI thread) with the new snapshot.</summary>
    public event Action<Settings>? Changed;

    /// <summary>
    /// Replaces the settings with <paramref name="change"/>(current). Call from the UI thread only. Ignored once the
    /// service is disposed (a window closing during exit).
    /// </summary>
    public void Update(Func<Settings, Settings> change)
    {
        if (disposed) return;
        Settings before = current;
        Settings after = change(before);
        // Record equality compares CustomMonitorIds by reference, so a rebuilt equal list still counts as a change;
        // that only costs a redundant save.
        if (after is null || after == before) return;
        Volatile.Write(ref current, after);
        // A change during a running save sets the flag again and re-arms the timer, so it is saved next time.
        Volatile.Write(ref savePending, 1);
        saveTimer.Change(SaveDelayMs, Timeout.Infinite);
        Changed?.Invoke(after);
    }

    /// <summary>
    /// Saves a pending change now. Safe from any thread. A failed save is logged and stays pending, without a retry
    /// timer: the next change or the save at Quit tries again.
    /// </summary>
    public void Flush()
    {
        lock (saveGate)
        {
            if (Interlocked.Exchange(ref savePending, 0) == 0) return;
            try
            {
                store.Save(Current);
            }
            catch (Exception exception)
            {
                // ISettingsStore.Save writes atomically, so a failure (IOException, UnauthorizedAccessException: the
                // folder is read-only, the disk full, the file held by another program) leaves the previous file intact.
                Trace.WriteLine($"[Settings] Saving to {store.Path} failed: {exception.Message}");
                Volatile.Write(ref savePending, 1);
            }
        }
    }

    /// <summary>Stops the save timer and saves any pending change.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        saveTimer.Dispose();
        Flush(); // also waits for a save already running on the timer thread
    }

    private static Settings Load(ISettingsStore store)
    {
        try
        {
            return store.Load() ?? new Settings();
        }
        catch (Exception exception)
        {
            // Load never throws by contract (doc 06 §1); defaults keep the app running if it does anyway.
            Trace.WriteLine($"[Settings] Loading failed, using defaults: {exception.Message}");
            return new Settings();
        }
    }
}
