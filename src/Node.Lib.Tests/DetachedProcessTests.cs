using System.Diagnostics;
using Shouldly;
using Xunit;

namespace Node.Lib.Tests;

/// <summary>A detached process runs, reports its id, and (on Windows) goes on without its handle.</summary>
public sealed class DetachedProcessTests
{
    private static (string Path, string[] Arguments) ShortLived(int exitCode) =>
        OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", ["/c", $"exit {exitCode}"])
            : ("/bin/sh", ["-c", $"exit {exitCode}"]);

    private static async Task<int> WaitForExitCode(DetachedProcess process)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (process.TryGetExitCode(out var code))
            {
                return code.Value;
            }
            await Task.Delay(25, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("The child did not exit");
    }

    [Fact]
    public async Task AChildRunsAndReportsItsIdAndExitCode()
    {
        var (path, arguments) = ShortLived(7);

        using var process = DetachedProcess.Start(path, arguments, Environment.CurrentDirectory, new Dictionary<string, string?>());

        process.Id.ShouldBeGreaterThan(0);
        (await WaitForExitCode(process)).ShouldBe(7);
    }

    [Fact]
    public void AChildOutlivesADisposedHandle()
    {
        // A child that stays alive a moment, started and let go of at once.
        var (path, arguments) = OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", new[] { "/c", "ping -n 3 127.0.0.1 >nul" })
            : ("/bin/sh", ["-c", "sleep 2"]);

        int id;
        using (var process = DetachedProcess.Start(path, arguments, Environment.CurrentDirectory, new Dictionary<string, string?>()))
        {
            id = process.Id;
        }

        using var survivor = Process.GetProcessById(id);
        survivor.HasExited.ShouldBeFalse();
        survivor.Kill(entireProcessTree: true);
    }

    [Fact]
    public void ACommandLineQuotesWhatTheCRuntimeWouldSplit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        DetachedProcess.CommandLine(@"C:\a b\x.exe", ["plain", "with space", "q\"uote", @"trail\", ""])
            .ShouldBe(@"""C:\a b\x.exe"" plain ""with space"" ""q\""uote"" trail\ """"");
    }
}
