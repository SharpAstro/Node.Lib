using System.Net;
using Microsoft.AspNetCore.Http;
using Node.Lib.Hosting;
using Shouldly;
using Xunit;

namespace Node.Lib.Tests;

/// <summary>Seeing is free, commands need control, this machine's routes stay its own, and the socket passes untouched.</summary>
public sealed class NodeAccessGateTests
{
    private const string Token = "a-held-token";

    private readonly List<NodeRouteAccess> _refused = [];

    private NodeAccessPolicy Policy(NodeRouteAccess access) => new NodeAccessPolicy
    {
        Classify = _ => access,
        MayCommand = context => ValueTask.FromResult(NodeAccessGate.BearerToken(context) == Token),
        RefuseAsync = (context, why) =>
        {
            _refused.Add(why);
            context.Response.StatusCode = 403;
            return Task.CompletedTask;
        },
    };

    private static DefaultHttpContext OverTcp(string? authorization = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalIpAddress = IPAddress.Parse("192.168.1.10");
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");
        if (authorization is not null)
        {
            context.Request.Headers.Authorization = authorization;
        }
        return context;
    }

    private async Task<bool> PassesAsync(HttpContext context, NodeRouteAccess access)
    {
        var passed = false;
        await NodeAccessGate.GateAsync(context, _ =>
        {
            passed = true;
            return Task.CompletedTask;
        }, Policy(access));
        return passed;
    }

    [Theory]
    [InlineData(NodeRouteAccess.Open)]
    [InlineData(NodeRouteAccess.Command)]
    [InlineData(NodeRouteAccess.ThisMachineOnly)]
    public async Task ARequestOverTheSocketPassesWhateverItsRoute(NodeRouteAccess access)
    {
        // The socket has no addresses.
        var context = new DefaultHttpContext();

        NodeAccessGate.CameOverTheSocket(context).ShouldBeTrue();
        (await PassesAsync(context, access)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnOpenRoutePassesFromTheNetworkWithoutAGrant()
    {
        (await PassesAsync(OverTcp(), NodeRouteAccess.Open)).ShouldBeTrue();
        _refused.ShouldBeEmpty();
    }

    [Fact]
    public async Task ACommandPassesWithAHeldGrantAndIsRefusedWithoutOne()
    {
        (await PassesAsync(OverTcp($"Bearer {Token}"), NodeRouteAccess.Command)).ShouldBeTrue();

        (await PassesAsync(OverTcp(), NodeRouteAccess.Command)).ShouldBeFalse();
        (await PassesAsync(OverTcp("Bearer someone-elses"), NodeRouteAccess.Command)).ShouldBeFalse();
        _refused.ShouldBe([NodeRouteAccess.Command, NodeRouteAccess.Command]);
    }

    [Fact]
    public async Task ThisMachinesRouteIsRefusedFromTheNetworkEvenWithAGrant()
    {
        (await PassesAsync(OverTcp($"Bearer {Token}"), NodeRouteAccess.ThisMachineOnly)).ShouldBeFalse();
        _refused.ShouldBe([NodeRouteAccess.ThisMachineOnly]);
    }

    [Theory]
    [InlineData("Bearer abc", "abc")]
    [InlineData("Bearer   abc  ", "abc")]
    [InlineData("Bearer ", null)]
    [InlineData("Basic abc", null)]
    [InlineData(null, null)]
    public void TheBearerTokenIsReadFromTheAuthorizationHeader(string? header, string? expected)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers.Authorization = header;
        }

        NodeAccessGate.BearerToken(context).ShouldBe(expected);
    }
}
