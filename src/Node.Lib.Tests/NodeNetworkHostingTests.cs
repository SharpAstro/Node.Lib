using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Node.Lib.Hosting;
using Shouldly;
using Xunit;

namespace Node.Lib.Tests;

/// <summary>The node's TCP listen binds only where <see cref="LocalOnlyMode"/> lets the process bind the network.</summary>
public sealed class NodeNetworkHostingTests
{
    [Fact]
    public void LocalOnlyBindsNothing()
    {
        var options = new KestrelServerOptions();

        options.ListenOnNodeNetwork(0, LocalOnlyMode.On).ShouldBeFalse();
    }

    [Fact]
    public async Task OtherwiseItListensOnTheNetwork()
    {
        var builder = WebApplication.CreateSlimBuilder();
        var bound = false;
        builder.WebHost.ConfigureKestrel(kestrel => bound = kestrel.ListenOnNodeNetwork(0, LocalOnlyMode.Off));
        await using var app = builder.Build();

        await app.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            bound.ShouldBeTrue();
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
            addresses.ShouldNotBeEmpty();
            addresses.ShouldNotContain(static address => address.EndsWith(":0"));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }
}
