namespace Rimlight.Core;

/// <summary>Immutable application settings; validation and persistence arrive in C7.</summary>
public sealed record Settings
{
    /// <summary>Version.</summary>
    public int Version { get; init; } = 1;
    /// <summary>Enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Animation (MusicSync | IdleGlow | Off).</summary>
    public AnimationMode Animation { get; init; } = AnimationMode.MusicSync; // MusicSync | IdleGlow | Off
    /// <summary>When Silent (IdleGlow | Hide).</summary>
    public SilentBehavior WhenSilent { get; init; } = SilentBehavior.IdleGlow; // IdleGlow | Hide

    /// <summary>Color Mode (AlbumArt | Manual).</summary>
    public ColorMode ColorMode { get; init; } = ColorMode.AlbumArt;          // AlbumArt | Manual
    /// <summary>Override Album Color.</summary>
    public bool OverrideAlbumColor { get; init; } = false;
    /// <summary>Primary Hex.</summary>
    public string PrimaryHex { get; init; } = "#7C5CFF";
    /// <summary>Secondary Hex.</summary>
    public string SecondaryHex { get; init; } = "#22D3EE";
    /// <summary>Primary Ratio (0.1..0.9).</summary>
    public float PrimaryRatio { get; init; } = 0.60f;                          // 0.1..0.9

    /// <summary>Core Thickness Dip (0..40).</summary>
    public float CoreThicknessDip { get; init; } = 6f;                         // 0..40
    /// <summary>Glow (0..1).</summary>
    public float Glow { get; init; } = 0.45f;                                  // 0..1
    /// <summary>Brightness (0.1..1).</summary>
    public float Brightness { get; init; } = 0.80f;                            // 0.1..1
    /// <summary>Sensitivity (0.25..2).</summary>
    public float Sensitivity { get; init; } = 1.0f;                            // 0.25..2
    /// <summary>Corner Radius Dip (0..40).</summary>
    public float CornerRadiusDip { get; init; } = 0f;                          // 0..40
    /// <summary>Cover Taskbar.</summary>
    public bool CoverTaskbar { get; init; } = true;

    /// <summary>Monitors (All | PrimaryOnly | Custom).</summary>
    public MonitorSelection Monitors { get; init; } = MonitorSelection.All;    // All | PrimaryOnly | Custom
    /// <summary>Custom Monitor Ids (stable device IDs, not indexes).</summary>
    public IReadOnlyList<string> CustomMonitorIds { get; init; } = [];         // stable device IDs, not indexes

    /// <summary>Fps Cap (30 | 60 | 120 | 0 = native).</summary>
    public int FpsCap { get; init; } = 60;                                     // 30 | 60 | 120 | 0 = native
    /// <summary>Pause In Fullscreen.</summary>
    public bool PauseInFullscreen { get; init; } = true;
    /// <summary>On Battery (Normal | Reduce | Pause).</summary>
    public BatteryBehavior OnBattery { get; init; } = BatteryBehavior.Reduce;  // Normal | Reduce | Pause
    /// <summary>Hide From Screen Capture.</summary>
    public bool HideFromScreenCapture { get; init; } = false;

    /// <summary>Launch At Startup.</summary>
    public bool LaunchAtStartup { get; init; } = true;
    /// <summary>Toggle Hotkey.</summary>
    public string ToggleHotkey { get; init; } = "Ctrl+Alt+L";
    /// <summary>Auto Update.</summary>
    public bool AutoUpdate { get; init; } = true;
    /// <summary>First Run Complete.</summary>
    public bool FirstRunComplete { get; init; } = false;
}
