// The C ABI of libRimlightCore: Rimlight.Core and the shared per-frame code, compiled with NativeAOT
// (src/Rimlight.Native/Exports.cs implements it: keep the two in sync, and bump RL_ABI_VERSION on any change).
//
// Strings are UTF-8. Inputs are NUL-terminated. A function that outputs a string writes it into the caller's buffer
// and returns its length in bytes, without the NUL; it writes only when capacity > length, so a caller whose buffer
// was too small calls again with length + 1. Every failure is a negative status. Nothing returned needs freeing.

#ifndef RIMLIGHT_CORE_H
#define RIMLIGHT_CORE_H

#include <stdint.h>

#include "glow_math.h"

#define RL_ABI_VERSION 10

static const int32_t RL_OK = 0;
static const int32_t RL_INVALID_ARGUMENT = -1; // a null pointer, a negative capacity or a string that isn't UTF-8
static const int32_t RL_IO_ERROR = -2;         // the settings file couldn't be written
static const int32_t RL_BAD_JSON = -3;         // the settings JSON couldn't be read
static const int32_t RL_INTERNAL = -99;        // an unexpected error inside the library

/// The library's ABI version; the app refuses to run against a library whose version isn't RL_ABI_VERSION.
int32_t rl_abi_version(void);

/// RL_ABI_VERSION as this header was compiled with (implemented in CRimlight.c, not in the library).
int32_t rl_header_abi_version(void);

/// The app's display name (Rimlight.Core's AppInfo.Name).
int32_t rl_app_name(char *buffer, int32_t capacity);

/// The version, from <Version> in src/Directory.Build.props.
int32_t rl_version(char *buffer, int32_t capacity);

/// The settings in directory/settings.json, validated, as JSON with the file's field names. A missing or bad file
/// gives the defaults (and a bad one is backed up as settings.bad.json). Never writes settings.json.
/// Returns the JSON's length, RL_INVALID_ARGUMENT or RL_INTERNAL.
int32_t rl_settings_load(const char *directory, char *buffer, int32_t capacity);

/// Validates the settings in json (the format rl_settings_load returns) and writes directory/settings.json
/// atomically, creating the directory. Fields left out keep their defaults. Returns RL_OK, RL_BAD_JSON, RL_IO_ERROR,
/// RL_INVALID_ARGUMENT or RL_INTERNAL.
int32_t rl_settings_save(const char *directory, const char *json);

// ---- Overlay loop (src/Rimlight.Native/Overlay/MacOverlayLoop.cs) ----
// The glow's per-frame decisions, the same code as Windows' (FramePacer, GlowConstants, the light engine). Create it on
// the main thread; apply_settings, set_demo, demo and set_paused are safe from any thread. Every other call belongs to
// the render thread, and the per-frame ones allocate nothing.

typedef intptr_t RlLoop; // 0 = none

/// One screen's overlay surface (SurfaceInfo).
typedef struct RlSurface {
    int32_t widthPx;      // the drawable's size in pixels
    int32_t heightPx;
    float pixelsPerPoint; // the screen's backing scale factor
    int32_t paused;       // 1 while a full-screen window covers this screen
    float notchLeftPt;    // the camera notch in points from the overlay's top-left: notchLeftPt to notchRightPt across,
    float notchRightPt;   // notchHeightPt down from the top; all 0 without one. The light goes round it.
    float notchHeightPt;
    int32_t pad;
} RlSurface;

/// What a frame needs from the render thread (FrameInfo).
typedef struct RlFrameInfo {
    int32_t gradientVersion; // upload rl_loop_gradient again when this changes
    float renderScale;       // 1, or 0.5 with "Reduce on battery": resize the drawables and set the surfaces again
    int32_t listening;       // 1 when the glow wants audio
    int32_t demo;            // 1 while demo mode is on
} RlFrameInfo;

/// How to wait after a frame (PaceInfo).
typedef struct RlPace {
    int32_t pace;           // RL_PACE_FULL, RL_PACE_SLOW or RL_PACE_BLOCK
    int32_t pad;
    double intervalSeconds; // the wait at RL_PACE_SLOW
    double fullRateHz;      // the display link's rate at RL_PACE_FULL
} RlPace;

typedef struct RlRgb {
    float r, g, b;
} RlRgb;

/// A beat's ripple (Core's Ripple): where it started on the perimeter (0..1, clockwise from the top-left corner), how
/// far each of its two fronts has run (in perimeters) and how bright it is now (0..1; 0 is none).
typedef struct RlRipple {
    float origin, radius, strength;
} RlRipple;

/// The Flow movement for one frame (Core's FlowMotion): how much of it shows (0..1; 0 lights the edge evenly), where
/// its comets are (0..1) and the beats' ripples.
typedef struct RlFlowMotion {
    float amount, cometPhase;
    RlRipple ripple0, ripple1, ripple2, ripple3;
} RlFlowMotion;

/// Core's LightState (linear RGB colours; the rest 0..1, thickness in points), its Flow movement last.
typedef struct RlLightState {
    RlRgb colorA;
    RlRgb colorB;
    float ratio, intensity, spread, coreThicknessDip, phase, pulse, visibility;
    RlFlowMotion flow;
} RlLightState;

GM_STATIC_ASSERT(sizeof(RlSurface) == 32, "RlSurface must match SurfaceInfo");
GM_STATIC_ASSERT(sizeof(RlFrameInfo) == 16, "RlFrameInfo must match FrameInfo");
GM_STATIC_ASSERT(sizeof(RlPace) == 24, "RlPace must match PaceInfo");
GM_STATIC_ASSERT(sizeof(RlRipple) == 12, "RlRipple must match Ripple");
GM_STATIC_ASSERT(sizeof(RlFlowMotion) == 56, "RlFlowMotion must match FlowMotion");
GM_STATIC_ASSERT(sizeof(RlLightState) == 108, "RlLightState must match LightState");

static const int32_t RL_PACE_FULL = 0;  // frames on the display link, at fullRateHz
static const int32_t RL_PACE_SLOW = 1;  // one frame after intervalSeconds
static const int32_t RL_PACE_BLOCK = 2; // no frames until an input changes

static const int32_t RL_SURFACE_NONE = 0;     // nothing to do
static const int32_t RL_SURFACE_DRAW = 1;     // draw the constants and present
static const int32_t RL_SURFACE_SHOW_AND_DRAW = 2; // show the window, then draw and present
static const int32_t RL_SURFACE_CLEAR = 3;    // draw the transparent frame (then hide the window, per surface_done)

/// A loop with the settings in directory/settings.json; demo 1 starts in demo mode. 0 on failure.
RlLoop rl_loop_create(const char *settingsDirectory, int32_t demo);
void rl_loop_destroy(RlLoop loop);

/// The loop's newest settings as JSON (the settings file's format).
int32_t rl_loop_settings(RlLoop loop, char *buffer, int32_t capacity);
/// Publishes new settings to the render thread; save 1 also validates and writes settings.json (the loop then gets
/// exactly what the file holds). Returns RL_OK, RL_BAD_JSON, RL_IO_ERROR (published, not saved) or RL_INVALID_ARGUMENT.
int32_t rl_loop_apply_settings(RlLoop loop, const char *json, int32_t save);
int32_t rl_loop_set_demo(RlLoop loop, int32_t on);
/// 1 while demo mode is on, 0 when off.
int32_t rl_loop_demo(RlLoop loop);
int32_t rl_loop_set_paused(RlLoop loop, int32_t paused);

// Render thread:
/// Replaces the screens' surfaces; each starts hidden. For half render scale, pass the halved size and pixels per point.
int32_t rl_loop_set_surfaces(RlLoop loop, const RlSurface *surfaces, int32_t count, double nowSeconds);
/// Pauses (1) or resumes (0) the glow on one screen; it fades over 300 ms and keeps its window.
int32_t rl_loop_set_surface_paused(RlLoop loop, int32_t index, int32_t paused, double nowSeconds);
/// The main screen's refresh rate, whether the Mac runs on battery (the OnBattery setting applies), and whether Low
/// Power Mode is on (always 30 fps at half scale, never paused).
int32_t rl_loop_set_display(RlLoop loop, double refreshHz, int32_t onBattery, int32_t lowPower);
/// Takes the inputs published since the last call: 1 when one changed (a frame may be due early), else 0.
int32_t rl_loop_apply_inputs(RlLoop loop, double nowSeconds);
/// 1 when an input arriving elapsedSeconds after the last frame should wait for the next frame at this pace.
int32_t rl_loop_input_waits(RlLoop loop, int32_t pace, double elapsedSeconds);
/// The time step for a frame elapsedSeconds after the last; frameDue 0 when an input brought it forward.
float rl_loop_frame_dt(RlLoop loop, double elapsedSeconds, int32_t frameDue);
/// Starts a frame. samples: mono audio drained since the last frame (count 0 for none) at sampleRate (0 while
/// nothing is captured). captureGeneration changes whenever the capture opens, closes or restarts: the analysis
/// starts over.
int32_t rl_loop_begin_frame(RlLoop loop, float dt, const float *samples, int32_t count, int32_t sampleRate,
                            int32_t captureGeneration, RlFrameInfo *info);
/// The 64 RGBA float texels of the palette gradient (linear RGB); the pointer stays valid for the loop's life.
const float *rl_loop_gradient(RlLoop loop);
/// What to do with surface index this frame (RL_SURFACE_*), and the constants to draw it with.
int32_t rl_loop_plan_surface(RlLoop loop, int32_t index, RlGlowConstants *constants);
/// Whether the planned frame was presented. Returns 1 when the window should be hidden now.
int32_t rl_loop_surface_done(RlLoop loop, int32_t index, int32_t presented, int64_t nowMs);
/// Ends the frame: how to wait for the next.
int32_t rl_loop_end_frame(RlLoop loop, double nowSeconds, RlPace *pace);

// ---- Settings window (src/Rimlight.Native/SettingsExports.cs) ----

/// The analyzer's latest features, for the level meter.
typedef struct RlAudioReading {
    float level, bass, beat; // 0..1
    int32_t silent;          // 1 while silent
    int32_t sequence;        // the same value twice means no new frame came; -1 before the first
} RlAudioReading;

GM_STATIC_ASSERT(sizeof(RlAudioReading) == 20, "RlAudioReading must match AudioReadingInfo");

/// Core's presets (doc 06 §1), in menu order. Any thread.
int32_t rl_presets_count(void);
int32_t rl_preset_name(int32_t index, char *buffer, int32_t capacity);
/// The settings JSON with preset index applied (its appearance fields only), as JSON.
int32_t rl_preset_apply(int32_t index, const char *settingsJson, char *buffer, int32_t capacity);

/// Keeps the audio capture and analysis running while Settings shows the meter or the preview. Any thread.
int32_t rl_loop_set_audio_wanted(RlLoop loop, int32_t wanted);
/// The analyzer's features from the latest frame. Any thread.
int32_t rl_loop_audio(RlLoop loop, RlAudioReading *reading);

/// The Appearance page's live preview: its own light engine and blender, fed the loop's settings, colors and audio.
/// Main thread.
typedef intptr_t RlPreview; // 0 = none
RlPreview rl_preview_create(RlLoop loop);
void rl_preview_destroy(RlPreview preview);
/// Advances the preview to nowSeconds and fills the constants for a card widthPx x heightPx (drawn as a 1280-point
/// screen). Returns 1 when the gradient changed (upload rl_preview_gradient again), 0 when not, or a negative status.
int32_t rl_preview_frame(RlPreview preview, double nowSeconds, int32_t widthPx, int32_t heightPx,
                         RlGlowConstants *constants);
const float *rl_preview_gradient(RlPreview preview);
/// Starts the preview's clock over when it is shown again: older audio counts as stale.
int32_t rl_preview_restart(RlPreview preview);

// ---- Now playing (src/Rimlight.Native/MediaExports.cs) ----

/// Extracts the album palette from a cover of width x height BGRA8 pixels (at most 4096 each; 64 x 64 is plenty) and
/// gives it to the glow. Returns 1 when published, 0 when the picture is "no art" (no opaque pixel: keep the previous
/// colours or retry), or a negative status. A blank or black-and-white cover is art and glows grey. Runs on the
/// calling thread: call it from a background queue.
int32_t rl_album_art(RlLoop loop, const uint8_t *bgra, int32_t width, int32_t height, const char *trackId);
/// No album colours: the manual ones show. Any thread.
int32_t rl_album_clear(RlLoop loop);
/// The album palette the glow has (linear RGB): 1 with the colours, 0 when there is none. Any thread.
int32_t rl_album_palette(RlLoop loop, RlRgb *primary, RlRgb *secondary);
/// The menu bar tooltip: "<name> — Title · Artist" while a track plays, "<name> — Waiting for music" otherwise
/// (TrayToolTip.cs). Pass NULL title and artist for no track.
int32_t rl_tooltip(const char *title, const char *artist, const char *sourceApp, int32_t isPlaying, char *buffer,
                   int32_t capacity);

// ---- Shell (src/Rimlight.Native/ShellExports.cs) ----

static const int32_t RL_HOTKEY_CTRL = 1;  // ⌃
static const int32_t RL_HOTKEY_ALT = 2;   // ⌥ (Option)
static const int32_t RL_HOTKEY_SHIFT = 4; // ⇧
static const int32_t RL_HOTKEY_WIN = 8;   // ⌘ (the settings file says "Win", as on Windows)

/// Parses the ToggleHotkey setting ("Ctrl+Alt+L") with the Windows rules: 1 with the modifiers and the Windows
/// virtual-key code, 0 for "" (no shortcut) or text that isn't an allowed shortcut. Any thread.
int32_t rl_hotkey_parse(const char *text, int32_t *modifiers, int32_t *virtualKey);
/// The setting's text for a shortcut, or RL_INVALID_ARGUMENT if it isn't allowed. Any thread.
int32_t rl_hotkey_format(int32_t modifiers, int32_t virtualKey, char *buffer, int32_t capacity);

static const int32_t RL_EDGE_GLOW = 0; // Settings' EdgeStyle: the soft glow (the default)
static const int32_t RL_EDGE_LINE = 1; // a bright line along the very edge, with a dimmer glow inside it

/// GlowConstants.Create alone, for tests: the constants for a light state on a surface (its notch included) in an edge
/// style (RL_EDGE_*), exactly as the loop computes them. The surface's paused is ignored.
int32_t rl_glow_constants(const RlLightState *state, const RlSurface *surface, float cornerRadiusDip, int32_t edgeStyle,
                          const RlRgb *meanColor, RlGlowConstants *constants);

// ---- Helpers implemented in C (CRimlight.c) ----

/// A lock-free counter shared between threads (drawables in flight, M1).
typedef struct RlcCounter RlcCounter;
RlcCounter *rlc_counter_create(void);
void rlc_counter_destroy(RlcCounter *counter);
int32_t rlc_counter_add(RlcCounter *counter, int32_t delta); // returns the new value
int32_t rlc_counter_load(const RlcCounter *counter);
void rlc_counter_store(RlcCounter *counter, int32_t value);

#endif
