using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>
/// One <see cref="AudioTuning"/> property as the visualizer's slider shows it (doc 03 §4: every parameter is
/// live-editable). Slider values are in display units (milliseconds for the short times, log₁₀ for the epsilon, the
/// exponent for the window size), and the range stays inside what the analyzer accepts, so a slider alone can only
/// break the two cross-field rules (band edges strictly increasing, silence exit at or above entry); the analyzer then
/// refuses the tuning and the visualizer says so.
/// </summary>
/// <param name="Name">The property's name, as in the JSON (H-015).</param>
/// <param name="Label">What the row says.</param>
/// <param name="Help">One line on what it does, for the tooltip and screen readers.</param>
/// <param name="Group">The section it is listed in.</param>
/// <param name="Minimum">The slider's minimum, in display units.</param>
/// <param name="Maximum">The slider's maximum, in display units.</param>
/// <param name="Step">One slider step, in display units.</param>
/// <param name="Read">The tuning's value in display units.</param>
/// <param name="Write">The tuning with a new value in display units.</param>
/// <param name="Format">The readout for a value in display units.</param>
internal sealed record TuningParameter(
    string Name,
    string Label,
    string Help,
    string Group,
    double Minimum,
    double Maximum,
    double Step,
    Func<AudioTuning, double> Read,
    Func<AudioTuning, double, AudioTuning> Write,
    Func<double, string> Format)
{
    /// <summary>The value on the slider's grid of steps.</summary>
    public double Snap(double value)
    {
        if (!double.IsFinite(value)) return Minimum;
        double steps = Math.Round((Math.Clamp(value, Minimum, Maximum) - Minimum) / Step);
        return Math.Round(Minimum + steps * Step, 6);
    }
}

/// <summary>Every <see cref="AudioTuning"/> parameter, in the order the visualizer lists them, and the JSON format.</summary>
internal static class TuningParameters
{
    /// <summary>The one parameter that is a setting: the visualizer's slider edits <see cref="Settings.Sensitivity"/> (H-007).</summary>
    public const string SensitivityName = nameof(AudioTuning.Sensitivity);

    private const string Bands = "Spectrum and bands";
    private const string Gain = "Auto-gain";
    private const string Envelopes = "Envelopes";
    private const string Mix = "Level mix";
    private const string Beats = "Beat detection";
    private const string Quiet = "Silence";

    // H-015: what tools/wav-analyze --tuning reads (tools/audio-tools/TuningJson.cs): every property, default naming,
    // indented.
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    // The reference reader's rules: any subset, keys in any case, comments and trailing commas allowed, unknown keys are
    // errors.
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>All 26 parameters. Ranges follow AudioAnalyzer's and AudioFrontEnd's validation (C2).</summary>
    public static IReadOnlyList<TuningParameter> All { get; } =
    [
        new(nameof(AudioTuning.WindowSize), "FFT window",
            "Samples per analysis: a power of two from 1024 to 16384. Larger resolves bass better but reacts later.",
            Bands, 10, 14, 1, t => Math.Log2(t.WindowSize), (t, v) => t with { WindowSize = 1 << (int)Math.Round(v) },
            v => (1 << (int)Math.Round(v)).ToString(CultureInfo.CurrentCulture)),
        new(nameof(AudioTuning.BassMinHz), "Bass from", "The bass band's lower edge (beats and Bass).",
            Bands, 0, 60, 1, t => t.BassMinHz, (t, v) => t with { BassMinHz = (float)v }, Hz),
        new(nameof(AudioTuning.BassMaxHz), "Bass to / Mid from", "Where the bass band ends and the mid band starts.",
            Bands, 80, 400, 5, t => t.BassMaxHz, (t, v) => t with { BassMaxHz = (float)v }, Hz),
        new(nameof(AudioTuning.MidMaxHz), "Mid to / High from", "Where the mid band ends and the high band starts.",
            Bands, 500, 6000, 50, t => t.MidMaxHz, (t, v) => t with { MidMaxHz = (float)v }, Hz),
        new(nameof(AudioTuning.HighMaxHz), "High to", "The high band's upper edge.",
            Bands, 6500, 20000, 250, t => t.HighMaxHz, (t, v) => t with { HighMaxHz = (float)v }, Hz),
        new(nameof(AudioTuning.MagnitudeEpsilon), "dB epsilon", "Added to band energy before it becomes dB (log scale).",
            Bands, -12, -3, 0.5, t => Math.Log10(t.MagnitudeEpsilon), (t, v) => t with { MagnitudeEpsilon = (float)Math.Pow(10, v) },
            v => Math.Pow(10, v).ToString("0.#e0", CultureInfo.CurrentCulture)),

        new(nameof(AudioTuning.PeakHalfLifeSeconds), "Peak half-life", "How fast each band's loudest recent level is forgotten.",
            Gain, 0.5, 15, 0.1, t => t.PeakHalfLifeSeconds, (t, v) => t with { PeakHalfLifeSeconds = (float)v }, Seconds),
        new(nameof(AudioTuning.FloorRiseDbPerSecond), "Floor rise", "How fast each band's quiet floor creeps up.",
            Gain, 0, 10, 0.1, t => t.FloorRiseDbPerSecond, (t, v) => t with { FloorRiseDbPerSecond = (float)v },
            v => string.Create(CultureInfo.CurrentCulture, $"{v:0.0} dB/s")),
        new(nameof(AudioTuning.MinimumRangeDb), "Minimum range", "The smallest floor-to-peak range a band is spread over.",
            Gain, 3, 40, 0.5, t => t.MinimumRangeDb, (t, v) => t with { MinimumRangeDb = (float)v }, Decibels),

        new(nameof(AudioTuning.LevelAttackSeconds), "Level attack", "How fast Level rises.",
            Envelopes, 1, 200, 1, t => t.LevelAttackSeconds * 1000.0, (t, v) => t with { LevelAttackSeconds = (float)(v / 1000) }, Milliseconds),
        new(nameof(AudioTuning.LevelReleaseSeconds), "Level release", "How fast Level falls.",
            Envelopes, 20, 1500, 10, t => t.LevelReleaseSeconds * 1000.0, (t, v) => t with { LevelReleaseSeconds = (float)(v / 1000) }, Milliseconds),
        new(nameof(AudioTuning.BassAttackSeconds), "Bass attack", "How fast Bass rises.",
            Envelopes, 1, 200, 1, t => t.BassAttackSeconds * 1000.0, (t, v) => t with { BassAttackSeconds = (float)(v / 1000) }, Milliseconds),
        new(nameof(AudioTuning.BassReleaseSeconds), "Bass release", "How fast Bass falls.",
            Envelopes, 20, 1500, 10, t => t.BassReleaseSeconds * 1000.0, (t, v) => t with { BassReleaseSeconds = (float)(v / 1000) }, Milliseconds),

        new(nameof(AudioTuning.LevelBassWeight), "Bass in Level", "The bass band's share of Level.",
            Mix, 0, 1, 0.01, t => t.LevelBassWeight, (t, v) => t with { LevelBassWeight = (float)v }, Weight),
        new(nameof(AudioTuning.LevelMidWeight), "Mid in Level", "The mid band's share of Level.",
            Mix, 0, 1, 0.01, t => t.LevelMidWeight, (t, v) => t with { LevelMidWeight = (float)v }, Weight),
        new(nameof(AudioTuning.LevelHighWeight), "High in Level", "The high band's share of Level.",
            Mix, 0, 1, 0.01, t => t.LevelHighWeight, (t, v) => t with { LevelHighWeight = (float)v }, Weight),

        new(SensitivityName, "Sensitivity",
            "The Motion setting: scales Level and lowers the beat threshold. Changing it here changes the setting.",
            Beats, 0.25, 2, 0.05, t => t.Sensitivity, (t, v) => t with { Sensitivity = (float)v },
            v => string.Create(CultureInfo.CurrentCulture, $"{v:0.00}×")),
        new(nameof(AudioTuning.FluxHistorySeconds), "Flux history", "How much recent flux the threshold is computed over.",
            Beats, 0.25, 4, 0.05, t => t.FluxHistorySeconds, (t, v) => t with { FluxHistorySeconds = (float)v }, Seconds),
        new(nameof(AudioTuning.FluxThresholdMultiplier), "Threshold multiplier", "Standard deviations above the mean flux a beat needs.",
            Beats, 0, 5, 0.05, t => t.FluxThresholdMultiplier, (t, v) => t with { FluxThresholdMultiplier = (float)v },
            v => string.Create(CultureInfo.CurrentCulture, $"{v:0.00}×")),
        new(nameof(AudioTuning.MinFlux), "Minimum flux", "The least flux for a beat, relative to the window's RMS.",
            Beats, 0, 1.5, 0.01, t => t.MinFlux, (t, v) => t with { MinFlux = (float)v }, Weight),
        new(nameof(AudioTuning.BeatRefractorySeconds), "Beat spacing", "The shortest time between two beats.",
            Beats, 0, 600, 5, t => t.BeatRefractorySeconds * 1000.0, (t, v) => t with { BeatRefractorySeconds = (float)(v / 1000) }, Milliseconds),
        new(nameof(AudioTuning.BeatDecaySeconds), "Beat decay", "How fast the Beat pulse fades.",
            Beats, 10, 600, 5, t => t.BeatDecaySeconds * 1000.0, (t, v) => t with { BeatDecaySeconds = (float)(v / 1000) }, Milliseconds),

        new(nameof(AudioTuning.NoPacketTimeoutSeconds), "No-packet timeout", "How long without audio packets counts as digital silence.",
            Quiet, 30, 1000, 10, t => t.NoPacketTimeoutSeconds * 1000.0, (t, v) => t with { NoPacketTimeoutSeconds = (float)(v / 1000) }, Milliseconds),
        new(nameof(AudioTuning.SilenceThresholdDb), "Silent below", "Sound under this level counts toward silence (dBFS).",
            Quiet, -90, -20, 1, t => t.SilenceThresholdDb, (t, v) => t with { SilenceThresholdDb = (float)v }, Decibels),
        new(nameof(AudioTuning.SilenceExitThresholdDb), "Sound above", "Sound over this level ends silence at once (dBFS); at least Silent below.",
            Quiet, -90, -20, 1, t => t.SilenceExitThresholdDb, (t, v) => t with { SilenceExitThresholdDb = (float)v }, Decibels),
        new(nameof(AudioTuning.SilenceHoldMs), "Silence after", "How long it must stay quiet before IsSilent.",
            Quiet, 0, 10000, 100, t => t.SilenceHoldMs, (t, v) => t with { SilenceHoldMs = (int)Math.Round(v) }, Milliseconds),
    ];

    /// <summary>The tuning as H-015's JSON: every property, default naming, indented.</summary>
    public static string ToJson(AudioTuning tuning) => JsonSerializer.Serialize(tuning, WriteOptions);

    /// <summary>
    /// Reads tuning JSON the way <c>wav-analyze --tuning</c> does (H-015): any subset of the properties on top of the
    /// defaults. Values are not range-checked here; the analyzer does that when the tuning is applied.
    /// </summary>
    /// <param name="json">The text.</param>
    /// <param name="tuning">The tuning, when the text is valid.</param>
    /// <param name="namesSensitivity">Whether the text sets Sensitivity (which is a setting, H-007).</param>
    /// <param name="error">Why the text isn't valid tuning JSON.</param>
    /// <returns>True when it is.</returns>
    public static bool TryParse(string json, out AudioTuning tuning, out bool namesSensitivity, out string error)
    {
        tuning = new AudioTuning();
        namesSensitivity = false;
        error = "";
        try
        {
            tuning = JsonSerializer.Deserialize<AudioTuning>(json, ReadOptions) ?? throw new JsonException("Expected a JSON object, not null.");
            using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
                namesSensitivity |= string.Equals(property.Name, SensitivityName, StringComparison.OrdinalIgnoreCase);
            return true;
        }
        catch (JsonException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static string Hz(double value) => string.Create(CultureInfo.CurrentCulture, $"{value:0} Hz");

    private static string Seconds(double value) => string.Create(CultureInfo.CurrentCulture, $"{value:0.00} s");

    private static string Milliseconds(double value) => string.Create(CultureInfo.CurrentCulture, $"{value:0} ms");

    private static string Decibels(double value) => string.Create(CultureInfo.CurrentCulture, $"{value:0.#} dB");

    private static string Weight(double value) => value.ToString("0.00", CultureInfo.CurrentCulture);
}
