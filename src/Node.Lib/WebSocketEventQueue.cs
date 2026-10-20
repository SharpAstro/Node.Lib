using System.Net.WebSockets;
using System.Threading.Channels;

namespace Node.Lib;

/// <summary>
/// One event-stream client's queue and its one sender: what a node writes an event to, so that a change never waits for
/// a socket. Lifted out of tianwen's and pilot's <c>EventHub</c>s, which keep their own policy (who is told, what is
/// logged, how a dropped client resumes) and share this.
/// </summary>
/// <remarks>
/// <para><b>A write never waits.</b> <see cref="TryEnqueue"/> answers at once; false means the client fell
/// <see cref="Capacity"/> messages behind (or was stopped), and the caller drops it with <see cref="Abort"/>.</para>
/// <para><b>Order holds.</b> <see cref="SendAllAsync"/> is the only sender, in the order the queue was written.</para>
/// <para><b>A stalled send ends.</b> Each send is bounded by the send timeout; one that outlasts it ends the sender with
/// the reason, and the caller drops the client.</para>
/// </remarks>
public sealed class WebSocketEventQueue
{
    // Never disposed: it has no timer, so there is nothing for Dispose to release, and a send that reads the token after
    // a dispose would throw rather than end.
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private readonly Channel<byte[]> _queue;
    private readonly TimeSpan _sendTimeout;

    /// <param name="socket">The client's socket, accepted and open.</param>
    /// <param name="capacity">How far behind the client may fall before <see cref="TryEnqueue"/> refuses.</param>
    /// <param name="sendTimeout">How long one send may take.</param>
    /// <param name="singleWriter">True when one thread at a time writes (a node that publishes under its own lock).</param>
    public WebSocketEventQueue(WebSocket socket, int capacity, TimeSpan sendTimeout, bool singleWriter = false)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Socket = socket;
        Capacity = capacity;
        _sendTimeout = sendTimeout;
        _queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = singleWriter,
            // TryWrite, never WriteAsync: a full queue answers false at once and the client is dropped.
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public WebSocket Socket { get; }

    /// <summary>How far behind the client may fall.</summary>
    public int Capacity { get; }

    /// <summary>Cancelled once the queue is stopped.</summary>
    public CancellationToken Stopping => _stopping.Token;

    /// <summary>Queues a message for the sender; false when the queue is full or stopped.</summary>
    public bool TryEnqueue(byte[] message) => _queue.Writer.TryWrite(message);

    /// <summary>
    /// The one sender: sends the queue in order until it is stopped. Returns null when it was stopped, else why it gave
    /// up (a send that outlasted the timeout, or the socket failing), for the caller to drop the client with.
    /// </summary>
    public async Task<string?> SendAllAsync()
    {
        try
        {
            await foreach (var message in _queue.Reader.ReadAllAsync(_stopping.Token))
            {
                using var send = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                send.CancelAfter(_sendTimeout);
                await Socket.SendAsync((ReadOnlyMemory<byte>)message, WebSocketMessageType.Text, endOfMessage: true, send.Token);
            }
            return null;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Stopped: whoever stopped it closes the socket.
            return null;
        }
        catch (OperationCanceledException)
        {
            return $"a send took longer than {_sendTimeout.TotalSeconds:0.#} s";
        }
        catch (Exception ex) when (ex is WebSocketException or InvalidOperationException or ObjectDisposedException)
        {
            return ex.Message;
        }
    }

    /// <summary>Stops the queue: no more writes, and the sender ends.</summary>
    public void Stop()
    {
        _queue.Writer.TryComplete();
        _stopping.Cancel();
    }

    /// <summary>Stops the queue and aborts the socket, so whoever reads it lets go too: how a client is dropped.</summary>
    public void Abort()
    {
        Stop();
        Socket.Abort();
    }
}
