namespace Rimlight.App.Visualizer;

/// <summary>
/// Hands the newest of a stream of objects from one writer thread to one reader thread without locks, waits or
/// allocations (doc 02: nothing on the render thread blocks). There are three buffers: the writer fills its back
/// buffer and swaps it into the middle slot; the reader swaps its front buffer for the middle one when that holds
/// something new. Each buffer belongs to exactly one side or to the middle slot at any time, so the reader never sees
/// a buffer the writer is filling. Frames the reader doesn't take in time are overwritten: the newest wins.
/// </summary>
/// <remarks>
/// <see cref="Interlocked.Exchange(ref int, int)"/> is a full fence on every platform .NET runs on (x64 and ARM64
/// included), so the writer's stores into a buffer happen before its publishing swap, and the reader's swap happens
/// before its loads.
/// </remarks>
/// <typeparam name="T">The buffer type, mutable and preallocated.</typeparam>
internal sealed class TripleBuffer<T> where T : class
{
    private const int IndexMask = 3;
    private const int Fresh = 4; // set in `middle` while it holds a buffer the reader hasn't taken

    private readonly T[] buffers;
    private int back;   // the writer's
    private int middle; // shared, swapped by both sides
    private int front;  // the reader's

    /// <summary>Creates the buffer from three distinct instances.</summary>
    public TripleBuffer(T first, T second, T third)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        ArgumentNullException.ThrowIfNull(third);
        if (ReferenceEquals(first, second) || ReferenceEquals(first, third) || ReferenceEquals(second, third))
            throw new ArgumentException("The three buffers must be distinct instances.");
        buffers = [first, second, third];
        back = 0;
        middle = 1;
        front = 2;
    }

    /// <summary>Writer only: the buffer to fill next. It belongs to the writer until <see cref="Publish"/>.</summary>
    public T Back => buffers[back];

    /// <summary>Writer only: hands the filled back buffer to the reader and takes another one to fill.</summary>
    public void Publish() => back = Interlocked.Exchange(ref middle, back | Fresh) & IndexMask;

    /// <summary>
    /// Reader only: takes the newest published buffer if one arrived since the last call. The buffer stays the
    /// reader's, unchanged, until its next successful call.
    /// </summary>
    /// <param name="latest">The newest buffer; when nothing new arrived, the one taken last (never yet written if none was).</param>
    /// <returns>True when <paramref name="latest"/> is new.</returns>
    public bool TryTake(out T latest)
    {
        if ((Volatile.Read(ref middle) & Fresh) == 0)
        {
            latest = buffers[front];
            return false;
        }
        front = Interlocked.Exchange(ref middle, front) & IndexMask;
        latest = buffers[front];
        return true;
    }
}
