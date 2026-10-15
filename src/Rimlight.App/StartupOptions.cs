using Rimlight.Platform.Shell;

namespace Rimlight.App;

/// <summary>
/// What the command line asks for: <c>--background</c> (the sign-in start, doc 06 §4), <c>--debug-visualizer</c>
/// (doc 03 §4) and <c>--demo</c> (doc 04 §5). Each flag is matched ignoring case; anything else is ignored, so
/// Velopack's own arguments pass through harmlessly.
/// </summary>
/// <param name="Background">Started at sign-in: open no window.</param>
/// <param name="ShowVisualizer">Open the debug visualizer.</param>
/// <param name="Demo">Enter demo mode: a synthetic 120 BPM beat and four palettes, for recordings.</param>
internal sealed record StartupOptions(bool Background, bool ShowVisualizer, bool Demo)
{
    /// <summary>Opens the debug visualizer.</summary>
    public const string VisualizerArgument = "--debug-visualizer";

    /// <summary>Starts in demo mode.</summary>
    public const string DemoArgument = "--demo";

    /// <summary>Reads the command line.</summary>
    /// <param name="arguments">The arguments, as WPF passes them to <c>OnStartup</c>.</param>
    /// <returns>The options; none set for an empty command line.</returns>
    public static StartupOptions Parse(IReadOnlyCollection<string> arguments) => new(
        arguments.Contains(StartupRegistration.BackgroundArgument, StringComparer.OrdinalIgnoreCase),
        arguments.Contains(VisualizerArgument, StringComparer.OrdinalIgnoreCase),
        arguments.Contains(DemoArgument, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// True when the start itself shows something (the visualizer, or the glow in demo mode), so it doesn't open
    /// Settings as a plain start by hand does.
    /// </summary>
    public bool ShowsSomething => ShowVisualizer || Demo;

    /// <summary>
    /// What to ask of a copy that is already running (<see cref="SingleInstance.Claim"/>): the visualizer and the demo
    /// when asked for, separated by a space; otherwise Settings for a start by hand, and nothing at sign-in.
    /// </summary>
    public string? HandOverCommand => (ShowVisualizer, Demo) switch
    {
        (true, true) => $"{SingleInstance.ShowVisualizerCommand} {SingleInstance.DemoCommand}",
        (true, false) => SingleInstance.ShowVisualizerCommand,
        (false, true) => SingleInstance.DemoCommand,
        _ => Background ? null : SingleInstance.ShowSettingsCommand,
    };
}
