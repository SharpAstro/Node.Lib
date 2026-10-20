using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Node.Lib.Hosting;

/// <summary>What a route is to a request that did not come over the node's socket.</summary>
public enum NodeRouteAccess
{
    /// <summary>Anyone who can reach the port may use it: watching, and asking for control.</summary>
    Open,

    /// <summary>A command: it needs control, a grant the node holds.</summary>
    Command,

    /// <summary>This machine's alone: never from the network, grant or not.</summary>
    ThisMachineOnly,
}

/// <summary>A node's rules for its routes, which <see cref="NodeAccessGate"/> applies.</summary>
public sealed class NodeAccessPolicy
{
    /// <summary>What a request's route is; asked only of a request that did not come over the socket.</summary>
    public required Func<HttpContext, NodeRouteAccess> Classify { get; init; }

    /// <summary>
    /// Whether a command may go through: the request's grant (its <see cref="NodeAccessGate.BearerToken"/>, say) is one
    /// the node holds. It may note who the grant is for on the context for the route to read.
    /// </summary>
    public required Func<HttpContext, ValueTask<bool>> MayCommand { get; init; }

    /// <summary>Writes the refusal, in the node's own envelope, for a command without control or a route that is this machine's.</summary>
    public required Func<HttpContext, NodeRouteAccess, Task> RefuseAsync { get; init; }
}

/// <summary>
/// The one place a request from the network is let through or refused: seeing is free, commands need control, and a
/// request over the node's socket passes untouched, its owner being this machine's person. A middleware, not a check in
/// each route, so a route added later is gated without remembering to be. Lifted out of pilot's and tianwen's nodes,
/// which keep their own rules (<see cref="NodeAccessPolicy"/>).
/// </summary>
public static class NodeAccessGate
{
    /// <summary>
    /// Whether a request came over the node's Unix domain socket, which has no addresses: a client of this machine. Over
    /// TCP both ends have one.
    /// </summary>
    public static bool CameOverTheSocket(HttpContext context) =>
        context.Connection.LocalIpAddress is null && context.Connection.RemoteIpAddress is null;

    /// <summary>The request's <c>Authorization: Bearer</c> token, or null.</summary>
    public static string? BearerToken(HttpContext context) =>
        context.Request.Headers.Authorization.ToString() is { } header && header.StartsWith("Bearer ", StringComparison.Ordinal)
            && header["Bearer ".Length..].Trim() is { Length: > 0 } token
            ? token
            : null;

    /// <summary>Gates every request after it by <paramref name="policy"/>.</summary>
    public static IApplicationBuilder UseNodeAccessGate(this IApplicationBuilder app, NodeAccessPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return app.Use((context, next) => GateAsync(context, next, policy));
    }

    /// <summary>The gate itself, for a host that composes its own pipeline.</summary>
    public static async Task GateAsync(HttpContext context, RequestDelegate next, NodeAccessPolicy policy)
    {
        if (CameOverTheSocket(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var access = policy.Classify(context);
        if (access is NodeRouteAccess.Open || (access is NodeRouteAccess.Command && await policy.MayCommand(context).ConfigureAwait(false)))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        await policy.RefuseAsync(context, access).ConfigureAwait(false);
    }
}
