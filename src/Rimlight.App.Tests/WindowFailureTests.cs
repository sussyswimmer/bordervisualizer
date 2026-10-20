using System.Windows.Markup;
using Rimlight.App.SettingsUi;
using Rimlight.Core;
using Xunit;

namespace Rimlight.App.Tests;

// What the user is told when Settings can't open (it used to fall back to the welcome window, which has no settings in
// it, and say nothing): what went wrong, down to the exception that caused it, and where the log is.
public sealed class WindowFailureTests
{
    [WindowsFact]
    public void TheMessageSaysWhatFailedAndWhereTheLogIs()
    {
        var cause = new KeyNotFoundException("Cannot find resource named 'Row'. Resource names are case sensitive.");
        var error = new XamlParseException("'Provide value on 'System.Windows.StaticResourceExtension' threw an exception.' "
            + "Line number '12' and line position '30'.", new InvalidOperationException("Wrapper", cause));

        string problem = WindowFailure.Describe(error);
        string message = WindowFailure.Message("its settings window", problem);

        Assert.StartsWith($"{AppInfo.Name} couldn't open its settings window.", message, StringComparison.Ordinal);
        Assert.Contains("XamlParseException", problem, StringComparison.Ordinal);
        Assert.Contains("Line number '12'", problem, StringComparison.Ordinal); // where in the XAML
        Assert.Contains("KeyNotFoundException: Cannot find resource named 'Row'", problem, StringComparison.Ordinal); // why
        string log = $@"%APPDATA%\{AppInfo.Name}\logs\{AppInfo.Name.ToLowerInvariant()}.log";
        Assert.Contains(log, message, StringComparison.Ordinal);
        Assert.Contains("[Settings]", message, StringComparison.Ordinal);
        Assert.Contains("[UiTheme]", message, StringComparison.Ordinal);
        // The folder as Windows names it, never the expanded path with the account's name in it.
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        Assert.DoesNotContain(appData, message, StringComparison.OrdinalIgnoreCase);
    }

    [WindowsFact]
    public void ASimpleExceptionIsDescribedOnceAndALongOneIsCut()
    {
        Assert.Equal("InvalidOperationException: Boom", WindowFailure.Describe(new InvalidOperationException("Boom")));
        string longOne = WindowFailure.Describe(new InvalidOperationException(new string('x', 5000)));
        Assert.True(longOne.Length <= 600, $"{longOne.Length} characters");
        Assert.EndsWith("…", longOne, StringComparison.Ordinal);
    }

    [WindowsFact]
    public void TheVisualizerPointsAtItsOwnLogLines()
    {
        string message = WindowFailure.Message("the audio visualizer", "Boom", "Visualizer");
        Assert.Contains("[Visualizer] or [UiTheme]", message, StringComparison.Ordinal);
    }
}
