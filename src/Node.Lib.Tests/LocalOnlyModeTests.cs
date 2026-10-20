using Shouldly;
using Xunit;

namespace Node.Lib.Tests;

/// <summary>Local-only: on for 1 or true, off for anything else, and the command line wins.</summary>
public sealed class LocalOnlyModeTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("0", false)]
    [InlineData("yes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void AValueIsOnFor1OrTrueOnly(string? value, bool on)
    {
        var mode = LocalOnlyMode.FromValue(value);

        mode.IsOn.ShouldBe(on);
        mode.MayBindTheNetwork.ShouldBe(!on);
    }

    [Fact]
    public void TheCommandLineTurnsItOnWhateverTheEnvironmentSays()
    {
        const string variable = "NODE_LIB_TESTS_LOCAL_ONLY_UNSET";

        LocalOnlyMode.Of(variable).ShouldBe(LocalOnlyMode.Off);
        LocalOnlyMode.Of(variable, toldOnTheCommandLine: true).ShouldBe(LocalOnlyMode.On);
    }
}
