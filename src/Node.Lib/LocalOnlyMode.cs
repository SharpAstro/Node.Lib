namespace Node.Lib;

/// <summary>
/// Whether a process binds nothing but loopback and its node's own socket: an application's environment variable
/// (<c>TIANWEN_LOCAL_ONLY=1</c>, <c>PILOT_LOCAL_ONLY=1</c>) or its <see cref="Argument"/>. Windows Defender Firewall asks
/// once per executable PATH when a process binds a non-loopback address, and never for loopback, so a build in a fresh
/// folder (a worktree, a scratch publish) asks again unless it is local-only; an agent's launcher turns it on for that.
/// Lifted out of tianwen's <c>LocalOnlyMode</c>.
/// </summary>
/// <remarks>
/// Decide it ONCE per process and hand the decision to whatever would bind a network address, rather than each site
/// reading the environment: a process is in the mode for its whole life or not at all.
/// </remarks>
public sealed record LocalOnlyMode(bool IsOn)
{
    /// <summary>The command-line switch for the mode, which a client passes to a node it starts.</summary>
    public const string Argument = "--local-only";

    public static LocalOnlyMode Off { get; } = new LocalOnlyMode(false);

    public static LocalOnlyMode On { get; } = new LocalOnlyMode(true);

    /// <summary>The mode an environment variable's value asks for: on for <c>1</c> or <c>true</c>, off for anything else.</summary>
    public static LocalOnlyMode FromValue(string? value) =>
        value?.Trim() is { } set && (set == "1" || set.Equals("true", StringComparison.OrdinalIgnoreCase)) ? On : Off;

    /// <summary>
    /// This process's mode: on when <paramref name="toldOnTheCommandLine"/>, else what <paramref name="variable"/> in
    /// its environment asks for.
    /// </summary>
    public static LocalOnlyMode Of(string variable, bool toldOnTheCommandLine = false) =>
        toldOnTheCommandLine ? On : FromValue(Environment.GetEnvironmentVariable(variable));

    /// <summary>
    /// Whether the process may bind a network address that is not loopback: listen on the LAN, join a multicast group,
    /// or broadcast. The one question every such site asks.
    /// </summary>
    public bool MayBindTheNetwork => !IsOn;
}
