using System.Net.WebSockets;
using System.Text;
using Shouldly;
using Xunit;

namespace Node.Lib.Tests;

/// <summary>One client's queue and its sender: a write never waits, order holds, and a stalled send ends.</summary>
public sealed class WebSocketEventQueueTests
{
    [Fact]
    public async Task TheSenderSendsTheQueueInOrderUntilStopped()
    {
        var socket = new FakeSocket();
        var queue = new WebSocketEventQueue(socket, capacity: 8, TimeSpan.FromSeconds(10));
        var sender = queue.SendAllAsync();

        foreach (var text in new[] { "1", "2", "3" })
        {
            queue.TryEnqueue(Encoding.UTF8.GetBytes(text)).ShouldBeTrue();
        }
        await socket.WaitForAsync(3);
        queue.Stop();

        (await sender).ShouldBeNull();
        socket.Sent.ShouldBe(["1", "2", "3"]);
    }

    [Fact]
    public async Task AFullQueueRefusesAtOnceRatherThanWaiting()
    {
        var socket = new FakeSocket { Stall = true };
        var queue = new WebSocketEventQueue(socket, capacity: 2, TimeSpan.FromSeconds(10));
        var sender = queue.SendAllAsync();

        // The first is taken by the stalled send; two more fill the queue; the next is refused.
        queue.TryEnqueue([1]).ShouldBeTrue();
        await socket.WaitForStartedAsync();
        queue.TryEnqueue([2]).ShouldBeTrue();
        queue.TryEnqueue([3]).ShouldBeTrue();
        queue.TryEnqueue([4]).ShouldBeFalse();

        queue.Abort();
        (await sender).ShouldBeNull();
        socket.State.ShouldBe(WebSocketState.Aborted);
    }

    [Fact]
    public async Task ASendThatOutlastsTheTimeoutEndsTheSenderWithWhy()
    {
        var socket = new FakeSocket { Stall = true };
        var queue = new WebSocketEventQueue(socket, capacity: 2, TimeSpan.FromMilliseconds(50));
        var sender = queue.SendAllAsync();

        queue.TryEnqueue([1]).ShouldBeTrue();

        (await sender.WaitAsync(TimeSpan.FromSeconds(10))).ShouldNotBeNull().ShouldContain("longer than");
    }

    [Fact]
    public async Task AStoppedQueueTakesNothingMore()
    {
        var queue = new WebSocketEventQueue(new FakeSocket(), capacity: 2, TimeSpan.FromSeconds(10));
        queue.Stop();

        queue.TryEnqueue([1]).ShouldBeFalse();
        queue.Stopping.IsCancellationRequested.ShouldBeTrue();
        (await queue.SendAllAsync()).ShouldBeNull();
    }

    /// <summary>Records what is sent, or, stalled, holds every send until it is cancelled.</summary>
    private sealed class FakeSocket : WebSocket
    {
        private readonly List<string> _sent = [];
        private readonly TaskCompletionSource _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocketState _state = WebSocketState.Open;

        public bool Stall { get; init; }

        public IReadOnlyList<string> Sent
        {
            get
            {
                lock (_sent)
                {
                    return [.. _sent];
                }
            }
        }

        public async Task WaitForAsync(int count)
        {
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (Sent.Count < count)
            {
                await Task.Delay(5, budget.Token);
            }
        }

        public Task WaitForStartedAsync() => _started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => _state;
        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) =>
            SendAsync((ReadOnlyMemory<byte>)buffer, messageType, endOfMessage, cancellationToken).AsTask();

        public override async ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            _started.TrySetResult();
            if (Stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            lock (_sent)
            {
                _sent.Add(Encoding.UTF8.GetString(buffer.Span));
            }
        }
    }
}
