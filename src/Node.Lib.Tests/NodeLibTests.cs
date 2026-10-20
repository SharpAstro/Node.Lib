using System.Net.Sockets;
using Shouldly;
using Xunit;

namespace Node.Lib.Tests;

/// <summary>Where a node's socket is, the lock that admits one node to it, and a listening probe.</summary>
public sealed class NodeLibTests : IDisposable
{
    // Short: a socket path must fit in 107 bytes, and the temp folder on Windows is already long.
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "nl-" + Guid.NewGuid().ToString("N")[..8]);

    public NodeLibTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void OneNodeHoldsTheLockAndASecondIsRefusedUntilItLetsGo()
    {
        var socket = Path.Combine(_folder, "node.sock");

        NodeLock.TryAcquire(socket, out var first, out _).ShouldBeTrue();
        using (first)
        {
            NodeLock.TryAcquire(socket, out _, out var refusal).ShouldBeFalse();
            refusal.ShouldNotBeNull();
            File.Exists(NodeSocketLocation.LockPathFor(socket)).ShouldBeTrue();
        }

        NodeLock.TryAcquire(socket, out var again, out _).ShouldBeTrue();
        again!.Dispose();
    }

    [Fact]
    public void OnlyTheHolderClearsAStaleSocket()
    {
        var socket = Path.Combine(_folder, "node.sock");
        File.WriteAllText(socket, "left by a dead node");
        NodeLock.TryAcquire(socket, out var held, out _).ShouldBeTrue();
        using (held)
        {
            held!.ClearStaleSocket();
        }
        File.Exists(socket).ShouldBeFalse();
    }

    [Fact]
    public async Task AHolderThatDoesNotAnswerIsDescribedByTheRefusal()
    {
        var described = await NodeLock.DescribeHolderAsync("/x/node.sock", new IOException("in use"), (_, _) => throw new IOException("nothing there"), TestContext.Current.CancellationToken);

        described.ShouldBe("Another process holds the lock on /x/node.sock and no node answered there: in use");
        (await NodeLock.DescribeHolderAsync("/x/node.sock", new IOException("in use"), (_, _) => Task.FromResult("node 42"), TestContext.Current.CancellationToken))
            .ShouldBe("node 42");
    }

    [Fact]
    public void ASocketIsNamedByArgumentThenEnvironmentElseTheDefault()
    {
        var variable = "NODE_LIB_TEST_" + Guid.NewGuid().ToString("N")[..8];
        var location = new NodeSocketLocation("node-lib-test", variable, () => _folder);

        location.DefaultPath.ShouldBe(Path.Combine(_folder, NodeSocketLocation.DefaultFileName));
        location.TryGetNamed(null, out var path).ShouldBeFalse();
        path.ShouldBe(location.DefaultPath);

        Environment.SetEnvironmentVariable(variable, Path.Combine(_folder, "env.sock"));
        try
        {
            location.TryGetNamed(null, out path).ShouldBeTrue();
            path.ShouldBe(Path.Combine(_folder, "env.sock"));
            location.Resolve(Path.Combine(_folder, "arg.sock")).ShouldBe(Path.Combine(_folder, "arg.sock"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact]
    public void APathTooLongForASocketAddressIsRefusedNamingIt()
    {
        NodeSocketLocation.TryValidate(new string('a', NodeSocketLocation.MaxPathBytes), out _).ShouldBeTrue();
        NodeSocketLocation.TryValidate(new string('a', NodeSocketLocation.MaxPathBytes + 1), out var error).ShouldBeFalse();
        error.ShouldNotBeNull().ShouldContain("longer than");
    }

    [Fact]
    public async Task TheProbeAndTheHandlerReachWhatListensOnTheSocket()
    {
        var socketPath = Path.Combine(_folder, "probe.sock");
        var ct = TestContext.Current.CancellationToken;
        (await NodeSocketLocation.IsListeningAsync(socketPath, ct)).ShouldBeFalse();

        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        var serving = Task.Run(async () =>
        {
            using var client = await listener.AcceptAsync(ct);
            client.Dispose();
            using var http = await listener.AcceptAsync(ct);
            var buffer = new byte[4096];
            await http.ReceiveAsync(buffer, ct);
            await http.SendAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), ct);
        }, ct);

        (await NodeSocketLocation.IsListeningAsync(socketPath, ct)).ShouldBeTrue();
        using var handler = NodeSocketLocation.CreateHandler(socketPath);
        using var httpClient = new HttpClient(handler) { BaseAddress = NodeSocketLocation.BaseAddress };
        (await httpClient.GetStringAsync("/", ct)).ShouldBe("ok");
        await serving;
    }
}
