using System.Collections.Immutable;
using System.Threading;

namespace Node.Lib;

/// <summary>
/// Lock-free fixed-capacity ring of the most recent <paramref name="capacity"/> items (index 0 = oldest): a node's
/// notification feed, a job's last lines. Backed by an <see cref="ImmutableArray{T}"/> that is atomically replaced on
/// <see cref="Add"/>, so readers take a torn-free snapshot with a single reference read instead of a lock-and-copy per
/// poll. Append pays the O(capacity) copy, which suits low-rate producers polled by high-rate readers. Lifted out of
/// tianwen's <c>TianWen.Lib.Sequencing.CircularBuffer</c>, which TianWen.Lib keeps for its browser build.
/// </summary>
public sealed class CircularBuffer<T>(int capacity)
{
    // The window and how many items were ever added, behind ONE reference, so a reader of both never sees one from
    // before an append and the other from after it.
    private State _state = new State([], 0);

    private sealed record State(ImmutableArray<T> Items, long Appended);

    /// <summary>Torn-free snapshot of the current window, oldest first.</summary>
    public ImmutableArray<T> Snapshot => Volatile.Read(ref _state).Items;

    public int Count => Snapshot.Length;

    /// <summary>
    /// The window and how many items were ever added, read together:
    /// item <c>i</c> of the window is the <c>Appended - Items.Length + i</c>th ever added, which is what lets a client that
    /// holds the first N ask for the rest and nothing else. The count never goes back, <see cref="Clear"/> included.
    /// </summary>
    public (ImmutableArray<T> Items, long Appended) Window
    {
        get
        {
            var state = Volatile.Read(ref _state);
            return (state.Items, state.Appended);
        }
    }

    public void Add(T item)
    {
        // CAS loop: each instance has a single logical writer today, but this keeps a
        // second writer from silently losing an append should that ever change.
        State current, next;
        do
        {
            current = Volatile.Read(ref _state);
            var items = current.Items.Length < capacity ? current.Items.Add(item) : current.Items.RemoveAt(0).Add(item);
            next = new State(items, current.Appended + 1);
        }
        while (Interlocked.CompareExchange(ref _state, next, current) != current);
    }

    public void Clear()
    {
        State current;
        do
        {
            current = Volatile.Read(ref _state);
        }
        while (Interlocked.CompareExchange(ref _state, new State([], current.Appended), current) != current);
    }
}
