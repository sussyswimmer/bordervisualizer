using System.Globalization;
using System.Reflection;
using Rimlight.Core;

namespace Rimlight.App.Visualizer;

/// <summary>How a slider position maps to a parameter's value.</summary>
internal enum SliderScale
{
    /// <summary>Position = value, rounded to the parameter's step.</summary>
    Linear,

    /// <summary>Position = log10(value), rounded to three significant digits: time constants and frequencies.</summary>
    Log,

    /// <summary>Position = log2(value), whole steps only: the FFT window.</summary>
    PowerOfTwo,
}

/// <summary>
/// One <see cref="AudioTuning"/> field as the visualizer shows it: a slider over a range that is useful for tuning
/// (the analyzer validates the actual limits), a value box and a short explanation from doc 03.
/// </summary>
/// <param name="Name">The property name, which is also its JSON key.</param>
/// <param name="Label">The row's label.</param>
/// <param name="Group">The section it is listed under.</param>
/// <param name="Description">What it does, as a tooltip.</param>
/// <param name="Minimum">The slider's lowest value.</param>
/// <param name="Maximum">The slider's highest value.</param>
/// <param name="Scale">How the slider maps to values.</param>
/// <param name="Step">Rounding for <see cref="SliderScale.Linear"/> values.</param>
/// <param name="Format">The value's display format.</param>
/// <param name="Unit">The unit after the value, or "".</param>
/// <param name="Get">Reads the value.</param>
/// <param name="With">Returns a copy with the value replaced.</param>
internal sealed record TuningParameter(
    string Name,
    string Label,
    string Group,
    string Description,
    double Minimum,
    double Maximum,
    SliderScale Scale,
    double Step,
    string Format,
    string Unit,
    Func<AudioTuning, double> Get,
    Func<AudioTuning, double, AudioTuning> With)
{
    /// <summary>The slider's minimum position.</summary>
    public double SliderMinimum => ToSlider(Minimum);

    /// <summary>The slider's maximum position.</summary>
    public double SliderMaximum => ToSlider(Maximum);

    /// <summary>The slider position for a value (outside the range, the slider shows its end).</summary>
    public double ToSlider(double value) => Scale switch
    {
        SliderScale.Log => Math.Log10(Math.Max(value, double.Epsilon)),
        SliderScale.PowerOfTwo => Math.Log2(Math.Max(value, 1)),
        _ => value,
    };

    /// <summary>The value at a slider position, rounded so a drag never produces values like 0.12345678.</summary>
    public double FromSlider(double position) => Round(Scale switch
    {
        SliderScale.Log => Math.Pow(10, position),
        SliderScale.PowerOfTwo => Math.Pow(2, Math.Round(position)),
        _ => position,
    });

    /// <summary>A typed or pasted value, rounded like a slider's (but not limited to the slider's range).</summary>
    public double Round(double value)
    {
        if (!double.IsFinite(value)) return value;
        switch (Scale)
        {
            case SliderScale.PowerOfTwo:
                return value;
            case SliderScale.Log when value != 0:
                double magnitude = Math.Pow(10, Math.Floor(Math.Log10(Math.Abs(value))) - 2);
                return Math.Round(value / magnitude) * magnitude;
            default:
                return Step > 0 ? Math.Round(value / Step) * Step : value;
        }
    }

    /// <summary>The value as typed into the box (no unit).</summary>
    public string Text(AudioTuning tuning) => Get(tuning).ToString(Format, CultureInfo.CurrentCulture);

    /// <summary>Reads a typed value in the current culture or the invariant one ("0,5" and "0.5" both work where , is the decimal separator).</summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        const NumberStyles Styles = NumberStyles.Float;
        return double.TryParse(text.Trim(), Styles, CultureInfo.CurrentCulture, out value) && double.IsFinite(value)
            || double.TryParse(text.Trim(), Styles, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }
}

/// <summary>Every <see cref="AudioTuning"/> field (doc 03 §4: "make all the parameters above live-editable").</summary>
internal static class TuningParameters
{
    /// <summary>The settings own this one (H-007): its row edits <see cref="Settings.Sensitivity"/>.</summary>
    public const string SensitivityName = nameof(AudioTuning.Sensitivity);

    /// <summary>The rows, grouped in the order of doc 03 §2.</summary>
    public static IReadOnlyList<TuningParameter> All { get; } =
    [
        new(nameof(AudioTuning.WindowSize), "Window size", "FFT window and bands",
            "FFT length in samples. Larger gives finer bass bins but reacts more slowly. Changing it restarts the analysis.",
            1024, 16384, SliderScale.PowerOfTwo, 0, "0", "samples",
            t => t.WindowSize, (t, v) => t with { WindowSize = (int)Math.Round(v) }),
        new(nameof(AudioTuning.BassMinHz), "Bass from", "FFT window and bands",
            "Lower edge of the bass band (beats and Bass).",
            0, 200, SliderScale.Linear, 1, "0", "Hz",
            t => t.BassMinHz, (t, v) => t with { BassMinHz = (float)v }),
        new(nameof(AudioTuning.BassMaxHz), "Bass to / Mid from", "FFT window and bands",
            "Upper edge of the bass band and lower edge of the mid band.",
            40, 500, SliderScale.Linear, 1, "0", "Hz",
            t => t.BassMaxHz, (t, v) => t with { BassMaxHz = (float)v }),
        new(nameof(AudioTuning.MidMaxHz), "Mid to / High from", "FFT window and bands",
            "Upper edge of the mid band and lower edge of the high band.",
            300, 8000, SliderScale.Log, 0, "0", "Hz",
            t => t.MidMaxHz, (t, v) => t with { MidMaxHz = (float)v }),
        new(nameof(AudioTuning.HighMaxHz), "High to", "FFT window and bands",
            "Upper edge of the high band.",
            2000, 24000, SliderScale.Log, 0, "0", "Hz",
            t => t.HighMaxHz, (t, v) => t with { HighMaxHz = (float)v }),

        new(nameof(AudioTuning.PeakHalfLifeSeconds), "Peak half-life", "Auto-gain",
            "How fast each band's running peak decays. Shorter adapts faster to quieter songs.",
            0.25, 30, SliderScale.Log, 0, "0.##", "s",
            t => t.PeakHalfLifeSeconds, (t, v) => t with { PeakHalfLifeSeconds = (float)v }),
        new(nameof(AudioTuning.FloorRiseDbPerSecond), "Floor rise", "Auto-gain",
            "How fast each band's floor rises (it drops at once).",
            0, 10, SliderScale.Linear, 0.1, "0.0", "dB/s",
            t => t.FloorRiseDbPerSecond, (t, v) => t with { FloorRiseDbPerSecond = (float)v }),
        new(nameof(AudioTuning.MinimumRangeDb), "Minimum range", "Auto-gain",
            "The smallest peak-to-floor range a band is normalized over. Larger keeps quiet passages darker.",
            1, 48, SliderScale.Linear, 0.5, "0.0", "dB",
            t => t.MinimumRangeDb, (t, v) => t with { MinimumRangeDb = (float)v }),
        new(nameof(AudioTuning.MagnitudeEpsilon), "Magnitude epsilon", "Auto-gain",
            "Added to magnitudes before converting them to dB.",
            1e-12, 1e-3, SliderScale.Log, 0, "0.##E+0", "",
            t => t.MagnitudeEpsilon, (t, v) => t with { MagnitudeEpsilon = (float)v }),

        new(nameof(AudioTuning.LevelAttackSeconds), "Level attack", "Envelopes",
            "Time constant while Level rises.",
            0.001, 0.5, SliderScale.Log, 0, "0.###", "s",
            t => t.LevelAttackSeconds, (t, v) => t with { LevelAttackSeconds = (float)v }),
        new(nameof(AudioTuning.LevelReleaseSeconds), "Level release", "Envelopes",
            "Time constant while Level falls.",
            0.01, 3, SliderScale.Log, 0, "0.###", "s",
            t => t.LevelReleaseSeconds, (t, v) => t with { LevelReleaseSeconds = (float)v }),
        new(nameof(AudioTuning.BassAttackSeconds), "Bass attack", "Envelopes",
            "Time constant while Bass rises.",
            0.001, 0.5, SliderScale.Log, 0, "0.###", "s",
            t => t.BassAttackSeconds, (t, v) => t with { BassAttackSeconds = (float)v }),
        new(nameof(AudioTuning.BassReleaseSeconds), "Bass release", "Envelopes",
            "Time constant while Bass falls.",
            0.01, 3, SliderScale.Log, 0, "0.###", "s",
            t => t.BassReleaseSeconds, (t, v) => t with { BassReleaseSeconds = (float)v }),

        new(nameof(AudioTuning.LevelBassWeight), "Bass weight", "Level mix",
            "How much the bass band adds to Level (the glow's brightness).",
            0, 1, SliderScale.Linear, 0.01, "0.00", "",
            t => t.LevelBassWeight, (t, v) => t with { LevelBassWeight = (float)v }),
        new(nameof(AudioTuning.LevelMidWeight), "Mid weight", "Level mix",
            "How much the mid band adds to Level.",
            0, 1, SliderScale.Linear, 0.01, "0.00", "",
            t => t.LevelMidWeight, (t, v) => t with { LevelMidWeight = (float)v }),
        new(nameof(AudioTuning.LevelHighWeight), "High weight", "Level mix",
            "How much the high band adds to Level.",
            0, 1, SliderScale.Linear, 0.01, "0.00", "",
            t => t.LevelHighWeight, (t, v) => t with { LevelHighWeight = (float)v }),

        new(nameof(AudioTuning.FluxHistorySeconds), "Flux history", "Beats",
            "How much bass flux the threshold's mean and spread are measured over.",
            0.25, 4, SliderScale.Linear, 0.05, "0.00", "s",
            t => t.FluxHistorySeconds, (t, v) => t with { FluxHistorySeconds = (float)v }),
        new(nameof(AudioTuning.FluxThresholdMultiplier), "Threshold (σ)", "Beats",
            "A beat needs flux above the mean plus this many standard deviations. Lower fires more beats.",
            0, 5, SliderScale.Linear, 0.05, "0.00", "σ",
            t => t.FluxThresholdMultiplier, (t, v) => t with { FluxThresholdMultiplier = (float)v }),
        new(nameof(AudioTuning.MinFlux), "Minimum flux", "Beats",
            "A beat also needs flux above this times the window's RMS (kicks score about 0.3 to 2).",
            0, 2, SliderScale.Linear, 0.01, "0.00", "× RMS",
            t => t.MinFlux, (t, v) => t with { MinFlux = (float)v }),
        new(nameof(AudioTuning.BeatRefractorySeconds), "Refractory", "Beats",
            "The shortest time between two beats (0.18 s allows up to about 333 BPM).",
            0, 1, SliderScale.Linear, 0.005, "0.000", "s",
            t => t.BeatRefractorySeconds, (t, v) => t with { BeatRefractorySeconds = (float)v }),
        new(nameof(AudioTuning.BeatDecaySeconds), "Beat decay", "Beats",
            "How fast Beat (the glow's pulse) falls after a beat.",
            0.01, 1, SliderScale.Log, 0, "0.###", "s",
            t => t.BeatDecaySeconds, (t, v) => t with { BeatDecaySeconds = (float)v }),
        new(SensitivityName, "Sensitivity", "Beats",
            "The Sensitivity setting (Settings, Motion): lowers the beat threshold and scales Level. Changing it here changes the setting.",
            0.25, 2, SliderScale.Linear, 0.05, "0.00", "×",
            t => t.Sensitivity, (t, v) => t with { Sensitivity = (float)v }),

        new(nameof(AudioTuning.SilenceThresholdDb), "Silent below", "Silence",
            "IsSilent turns on after the level stays below this for the hold time.",
            -100, -20, SliderScale.Linear, 1, "0", "dBFS",
            t => t.SilenceThresholdDb, (t, v) => t with { SilenceThresholdDb = (float)v }),
        new(nameof(AudioTuning.SilenceExitThresholdDb), "Sound above", "Silence",
            "IsSilent turns off as soon as the level rises above this (at least Silent below).",
            -100, -20, SliderScale.Linear, 1, "0", "dBFS",
            t => t.SilenceExitThresholdDb, (t, v) => t with { SilenceExitThresholdDb = (float)v }),
        new(nameof(AudioTuning.SilenceHoldMs), "Silence hold", "Silence",
            "How long it must stay quiet before IsSilent turns on.",
            0, 10000, SliderScale.Linear, 50, "0", "ms",
            t => t.SilenceHoldMs, (t, v) => t with { SilenceHoldMs = (int)Math.Round(v) }),
        new(nameof(AudioTuning.NoPacketTimeoutSeconds), "No-packet timeout", "Silence",
            "Without audio packets for this long, the gap counts as digital silence.",
            0.03, 2, SliderScale.Log, 0, "0.###", "s",
            t => t.NoPacketTimeoutSeconds, (t, v) => t with { NoPacketTimeoutSeconds = (float)v }),
    ];

    /// <summary>
    /// <see cref="AudioTuning"/> properties without a row (none today). A field added by a later contract change then
    /// still shows up in the window and in the copied JSON, which is written from the record itself.
    /// </summary>
    public static IReadOnlyList<string> Missing()
    {
        var names = new HashSet<string>(All.Select(p => p.Name), StringComparer.Ordinal);
        return [.. typeof(AudioTuning).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .Where(name => !names.Contains(name))];
    }
}
