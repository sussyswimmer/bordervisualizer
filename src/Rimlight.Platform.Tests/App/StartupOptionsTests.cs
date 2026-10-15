using System.Text;
using Rimlight.App;
using Rimlight.Platform.Shell;
using Xunit;

namespace Rimlight.Platform.Tests;

// StartupOptions (K6/K8): the command line, what a second start hands to the running copy, and the window a start opens.
public sealed class StartupOptionsTests
{
    [Fact]
    public void FlagsAreMatchedIgnoringCaseAndEverythingElseIsIgnored()
    {
        Assert.Equal(new StartupOptions(false, false, false), StartupOptions.Parse([]));
        Assert.Equal(new StartupOptions(true, false, false), StartupOptions.Parse(["--background"]));
        Assert.Equal(new StartupOptions(false, true, true), StartupOptions.Parse(["--DEMO", "--Debug-Visualizer"]));
        // Velopack's own arguments, and anything unknown, pass through harmlessly.
        Assert.Equal(new StartupOptions(true, false, false), StartupOptions.Parse(["--veloapp-firstrun", "--background", "x"]));
        Assert.Equal(new StartupOptions(false, false, false), StartupOptions.Parse(["-demo", "demo", "--demo=1"]));
    }

    [Theory]
    [InlineData(new string[0], "show-settings")]                                     // a start by hand opens Settings there
    [InlineData(new[] { "--background" }, null)]                                      // sign-in: just leave
    [InlineData(new[] { "--debug-visualizer" }, "show-visualizer")]
    [InlineData(new[] { "--demo" }, "demo")]
    [InlineData(new[] { "--demo", "--debug-visualizer" }, "show-visualizer demo")]
    [InlineData(new[] { "--background", "--demo" }, "demo")]
    public void ASecondStartHandsOverWhatItWasAskedFor(string[] arguments, string? command) =>
        Assert.Equal(command, StartupOptions.Parse(arguments).HandOverCommand);

    [Fact]
    public void EveryHandOverFitsTheRunningCopysLimit()
    {
        // The running copy reads at most MaxCommandBytes of a line, newline included, and splits it on spaces.
        string[][] starts = [[], ["--background"], ["--demo"], ["--debug-visualizer"], ["--demo", "--debug-visualizer"]];
        foreach (string[] start in starts)
        {
            string? command = StartupOptions.Parse(start).HandOverCommand;
            if (command is null) continue;
            Assert.True(Encoding.UTF8.GetByteCount(command + "\n") <= SingleInstance.MaxCommandBytes, command);
            Assert.All(command.Split(' '), part => Assert.Contains(part,
                new[] { SingleInstance.ShowSettingsCommand, SingleInstance.ShowVisualizerCommand, SingleInstance.DemoCommand }));
        }
    }

    [Theory]
    // first run complete?, arguments → window (StartupWindow is internal, so it is named)
    [InlineData(false, new string[0], "Welcome")]
    [InlineData(false, new[] { "--background" }, "Welcome")]   // the first run always says hello
    [InlineData(false, new[] { "--demo" }, "Welcome")]
    [InlineData(true, new string[0], "Settings")]              // later starts by hand open Settings
    [InlineData(true, new[] { "--background" }, "None")]       // sign-in opens nothing
    [InlineData(true, new[] { "--demo" }, "None")]             // the demo or the visualizer is the window
    [InlineData(true, new[] { "--debug-visualizer" }, "None")]
    public void EachStartOpensOneWindowAtMost(bool firstRunComplete, string[] arguments, string window) =>
        Assert.Equal(window, StartupOptions.Parse(arguments).WindowAtStart(firstRunComplete).ToString());
}
