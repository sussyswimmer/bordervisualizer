using System.Reflection;
using System.Text.Json;
using Rimlight.App.Visualizer;
using Rimlight.AudioTools;
using Rimlight.Core;
using Xunit;

namespace Rimlight.Platform.Tests;

// TuningParameters (K8, H-015): the visualizer's sliders and "Copy params as JSON" / "Paste JSON", against the real
// analyzer's validation and the reference format in tools/audio-tools (what wav-analyze --tuning reads).
public sealed class TuningParametersTests
{
    private static readonly PropertyInfo[] Properties = typeof(AudioTuning).GetProperties(BindingFlags.Public | BindingFlags.Instance);

    [Fact]
    public void ThereIsOneSliderPerTuningProperty()
    {
        Assert.Equal(Properties.Length, TuningParameters.All.Count);
        Assert.Equal(Properties.Select(p => p.Name).Order(), TuningParameters.All.Select(p => p.Name).Order());
        Assert.Contains(TuningParameters.All, p => p.Name == TuningParameters.SensitivityName);
    }

    [Fact]
    public void CopyWritesExactlyTheReferenceJson()
    {
        // H-015: every property, default naming, indented; byte for byte what TuningJson writes.
        var tuning = new AudioTuning { Sensitivity = 1.25f, FluxThresholdMultiplier = 1.8f, WindowSize = 4096, SilenceHoldMs = 1500 };
        Assert.Equal(TuningJson.Serialize(tuning), TuningParameters.ToJson(tuning));
        Assert.Equal(TuningJson.Serialize(new AudioTuning()), TuningParameters.ToJson(new AudioTuning()));
    }

    [Fact]
    public void ATuningThatChangesEveryPropertyRoundTripsBothWays()
    {
        AudioTuning changed = new();
        foreach (TuningParameter parameter in TuningParameters.All)
        {
            double value = parameter.Read(changed);
            double other = parameter.Snap(value + parameter.Step) is var up && up != value ? up : parameter.Snap(value - parameter.Step);
            changed = parameter.Write(changed, other);
        }
        Assert.Equal(Properties.Length, TuningJson.Overrides(changed).Count);

        string json = TuningParameters.ToJson(changed);
        Assert.Equal(changed, TuningJson.Parse(json));
        Assert.True(TuningParameters.TryParse(json, out AudioTuning pasted, out bool namesSensitivity, out _));
        Assert.Equal(changed, pasted);
        Assert.True(namesSensitivity);
    }

    [Fact]
    public void PasteReadsWhatTheReferenceReaderReads()
    {
        const string json = """
            {
              // a subset, in any case, with comments and trailing commas
              "beatdecayseconds": 0.2,
              "WINDOWSIZE": 4096,
            }
            """;
        Assert.True(TuningParameters.TryParse(json, out AudioTuning tuning, out bool namesSensitivity, out _));
        Assert.Equal(TuningJson.Parse(json), tuning);
        Assert.Equal(0.2f, tuning.BeatDecaySeconds);
        Assert.Equal(4096, tuning.WindowSize);
        Assert.Equal(new AudioTuning().Sensitivity, tuning.Sensitivity);
        Assert.False(namesSensitivity);

        Assert.True(TuningParameters.TryParse("""{ "sensitivity": 1.5 }""", out tuning, out namesSensitivity, out _));
        Assert.True(namesSensitivity);
        Assert.Equal(1.5f, tuning.Sensitivity);
    }

    [Theory]
    [InlineData("""{ "NoSuchKnob": 1 }""")]   // unknown keys are errors
    [InlineData("null")]
    [InlineData("[1, 2]")]
    [InlineData("""{ "WindowSize": "big" }""")]
    [InlineData("not json")]
    public void PasteRefusesWhatTheReferenceReaderRefuses(string json)
    {
        Assert.False(TuningParameters.TryParse(json, out _, out _, out string error));
        Assert.False(string.IsNullOrEmpty(error));
        Assert.ThrowsAny<JsonException>(() => TuningJson.Parse(json));
    }

    [Fact]
    public void EveryDefaultLiesOnItsSlidersGridAndWritesBackExactly()
    {
        var defaults = new AudioTuning();
        foreach (TuningParameter parameter in TuningParameters.All)
        {
            double value = parameter.Read(defaults);
            Assert.InRange(value, parameter.Minimum, parameter.Maximum);
            Assert.Equal(value, parameter.Snap(value), 4); // display units of a float, e.g. 0.03f × 1000 = 29.9999993
            Assert.Equal(defaults, parameter.Write(defaults, parameter.Snap(value)));
        }
    }

    [Fact]
    public void EveryStepOfEverySliderIsATuningTheAnalyzerAccepts()
    {
        // Ranges stay inside the analyzer's validation, so one slider alone can only break the cross-field rules, which
        // the two silence thresholds may on purpose (exit must be at or above entry): the visualizer then says so.
        IAudioAnalyzer analyzer = CoreFactory.CreateAnalyzer();
        var defaults = new AudioTuning();
        foreach (TuningParameter parameter in TuningParameters.All)
        {
            bool crossField = parameter.Name is nameof(AudioTuning.SilenceThresholdDb) or nameof(AudioTuning.SilenceExitThresholdDb);
            int steps = (int)Math.Round((parameter.Maximum - parameter.Minimum) / parameter.Step);
            for (int i = 0; i <= steps; i++)
            {
                double value = parameter.Snap(parameter.Minimum + i * parameter.Step);
                AudioTuning tuning = parameter.Write(defaults, value);
                try
                {
                    analyzer.Tuning = tuning;
                }
                catch (ArgumentException exception) when (crossField)
                {
                    Assert.Contains("Silence", exception.Message, StringComparison.OrdinalIgnoreCase);
                }
                Assert.Equal(value, parameter.Snap(parameter.Read(tuning)), 6);
                Assert.False(string.IsNullOrEmpty(parameter.Format(value)));
            }
        }
        analyzer.Tuning = defaults;
    }
}
