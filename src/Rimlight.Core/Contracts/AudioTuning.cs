namespace Rimlight.Core;

/// <summary>Immutable, live-replaceable audio parameters from doc 03.</summary>
public sealed record AudioTuning
{
    /// <summary>FFT window size in samples; must be a power of two.</summary>
    public int WindowSize { get; init; } = 2048;

    /// <summary>Bass lower boundary in Hz.</summary>
    public float BassMinHz { get; init; } = 30f;

    /// <summary>Bass upper boundary in Hz.</summary>
    public float BassMaxHz { get; init; } = 150f;

    /// <summary>Mid upper boundary in Hz; its lower boundary is BassMaxHz.</summary>
    public float MidMaxHz { get; init; } = 2000f;

    /// <summary>High upper boundary in Hz; its lower boundary is MidMaxHz.</summary>
    public float HighMaxHz { get; init; } = 12000f;

    /// <summary>Epsilon added before converting magnitude to dB.</summary>
    public float MagnitudeEpsilon { get; init; } = 1e-9f;

    /// <summary>Auto-gain peak half-life in seconds.</summary>
    public float PeakHalfLifeSeconds { get; init; } = 4f;

    /// <summary>Auto-gain floor rise rate in dB per second.</summary>
    public float FloorRiseDbPerSecond { get; init; } = 1f;

    /// <summary>Minimum normalization range in dB.</summary>
    public float MinimumRangeDb { get; init; } = 12f;

    /// <summary>Level attack time constant in seconds.</summary>
    public float LevelAttackSeconds { get; init; } = 0.030f;

    /// <summary>Level release time constant in seconds.</summary>
    public float LevelReleaseSeconds { get; init; } = 0.250f;

    /// <summary>Bass attack time constant in seconds.</summary>
    public float BassAttackSeconds { get; init; } = 0.015f;

    /// <summary>Bass release time constant in seconds.</summary>
    public float BassReleaseSeconds { get; init; } = 0.180f;

    /// <summary>Bass contribution to level.</summary>
    public float LevelBassWeight { get; init; } = 0.55f;

    /// <summary>Mid contribution to level.</summary>
    public float LevelMidWeight { get; init; } = 0.35f;

    /// <summary>High contribution to level.</summary>
    public float LevelHighWeight { get; init; } = 0.10f;

    /// <summary>Spectral-flux history duration in seconds.</summary>
    public float FluxHistorySeconds { get; init; } = 1f;

    /// <summary>Standard deviations above mean for beat detection.</summary>
    public float FluxThresholdMultiplier { get; init; } = 1.5f;

    /// <summary>Minimum flux for beats; provisional K0 default, to be tuned in C2.</summary>
    public float MinFlux { get; init; } = 0.01f;

    /// <summary>Minimum interval between beats in seconds.</summary>
    public float BeatRefractorySeconds { get; init; } = 0.180f;

    /// <summary>Beat pulse decay time constant in seconds.</summary>
    public float BeatDecaySeconds { get; init; } = 0.120f;

    /// <summary>Sensitivity, 0.25..2; scales level and inversely scales the flux multiplier.</summary>
    public float Sensitivity { get; init; } = 1f;

    /// <summary>Time without packets before treating input as silent.</summary>
    public float NoPacketTimeoutSeconds { get; init; } = 0.100f;

    /// <summary>Silence entry threshold in dBFS.</summary>
    public float SilenceThresholdDb { get; init; } = -60f;

    /// <summary>Silence exit threshold in dBFS.</summary>
    public float SilenceExitThresholdDb { get; init; } = -55f;

    /// <summary>Sustained silence duration in milliseconds.</summary>
    public int SilenceHoldMs { get; init; } = 2000;
}
