# 04 — Overlay Rendering

One borderless, transparent, click-through window per enabled monitor. Each one is rendered with Direct3D 11 into a **composition swap chain** with per-pixel alpha, displayed through **DirectComposition**. Do **not** use WPF `AllowsTransparency` windows or `UpdateLayeredWindow` for the overlay. Both are CPU-bound and too slow at 4K/60.

## 1. The window

Create it with raw Win32 (CsWin32), not WPF:

```
dwStyle   = WS_POPUP
dwExStyle = WS_EX_NOREDIRECTIONBITMAP   // DirectComposition provides the content
          | WS_EX_LAYERED | WS_EX_TRANSPARENT   // together: mouse input passes through
          | WS_EX_TOPMOST
          | WS_EX_TOOLWINDOW            // not in taskbar / Alt-Tab
          | WS_EX_NOACTIVATE            // never takes focus
```

- After creation, call `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)`. Layered windows don't show until you set an attribute.
- Handle `WM_NCHITTEST` → return `HTTRANSPARENT`, and `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`. These are belt-and-braces with the styles above.
- Position with `SetWindowPos(HWND_TOPMOST, rcMonitor..., SWP_NOACTIVATE | SWP_SHOWWINDOW)` using the **physical-pixel** monitor rect from `GetMonitorInfo`.
- The process is **Per-Monitor-V2 DPI aware** (app manifest). Convert DIP settings (thickness) to pixels with `GetDpiForMonitor` per monitor.
- Setting **Cover taskbar** (default on): use `rcMonitor`. When off, use `rcWork`.
- Exclude from screen capture only when the setting is on: `SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`.
- Exclude from DWM peek / Aero Shake: `DwmSetWindowAttribute(DWMWA_EXCLUDED_FROM_PEEK, true)`.
- **Staying on top:** other topmost windows can cover the overlay. Hook `EVENT_SYSTEM_FOREGROUND` with `SetWinEventHook(WINEVENT_OUTOFCONTEXT)` and re-assert `HWND_TOPMOST` (`SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE`), throttled to at most once per 250 ms.
- **Verify early (Phase 1):** clicks, scroll, drag-and-drop, and text selection all work through the overlay. Test in Explorer, a browser, and a game. If `WS_EX_LAYERED` + `NOREDIRECTIONBITMAP` misbehaves on a Windows build, document it in PROGRESS.md and try `WS_EX_TRANSPARENT` alone, with `HTTRANSPARENT`.

## 2. D3D11 + DirectComposition setup (Vortice)

```
D3D11CreateDevice(hardware, BGRA_SUPPORT)     // one device shared by all overlays
  → IDXGIDevice → IDXGIFactory2
  → CreateSwapChainForComposition(device, desc):
        Format      = B8G8R8A8_UNORM
        AlphaMode   = PREMULTIPLIED
        SwapEffect  = FLIP_SEQUENTIAL
        BufferCount = 2
        Flags       = FRAME_LATENCY_WAITABLE_OBJECT
  → SetMaximumFrameLatency(1)
DCompositionCreateDevice(dxgiDevice) → IDCompositionDevice
  → CreateTargetForHwnd(hwnd, topmost: true) → target
  → CreateVisual() → visual.SetContent(swapChain); target.SetRoot(visual); Commit()
```

- One D3D device and one `IDCompositionDevice` are shared by all monitors. Each overlay has its own swap chain + target + visual.
- **Render scale** (setting: Full / Half; default Full; Half when "Reduce on battery" is active): Half creates the swap chain at ½ width and height and sets a 2× scale transform on the visual. The glow is soft, so this is nearly invisible and quarters the fill cost.
- On resize or DPI change, `ResizeBuffers`. On `DXGI_ERROR_DEVICE_REMOVED/RESET`, rebuild everything (doc 02).

## 3. The glow shader

Draw one full-screen triangle (no vertex buffer, using `SV_VertexID`). The pixel shader computes everything from a constant buffer:

```hlsl
cbuffer Light : register(b0)
{
    float2 ScreenPx;        // swap chain size in pixels
    float  CornerRadiusPx;  // 0 = square corners
    float  CoreThicknessPx; // solid line
    float  SpreadPx;        // glow reach (e-folding distance)
    float  Intensity;       // 0..1
    float  Pulse;           // 0..1 beat kick
    float  Phase;           // 0..1 gradient rotation
    float  Ratio;           // share of ColorA, e.g. 0.6
    float3 ColorA;  float _pad0;   // linear RGB
    float3 ColorB;  float _pad1;
    float  Time;  float3 _pad2;
};
```

Per pixel:
1. **Distance to the edge**, `d`. Use the signed distance of a rounded rectangle (inset by 0) and take `d = -sdRoundBox(p - center, halfSize, CornerRadiusPx)`. This gives 0 at the edge and grows inward.
2. **Shape:**
   - `core = 1 - smoothstep(CoreThicknessPx - 1, CoreThicknessPx + 1, d)` (antialiased solid line)
   - `spread = SpreadPx * (1 + 0.35 * Pulse)` (the beat briefly pushes the light further in)
   - `glow = exp(-d / max(spread, 1))`
   - `a = saturate(max(core, glow * 0.85)) * Intensity * (1 + 0.25 * Pulse)`, then clamp to 1
   - *Clarification (Maxwell's feedback, rounds 1 and 2): the solid core read as "a very obvious line or bar". Round 1 replaced it with a soft exponential band on the exponential glow, which then read as "a thin bright line right at the screen edge, then the glow fades in behind it": an exponential is steepest at the edge, so it packs the brightest light into the outermost few pixels. The shape is now one light with a flat soft shoulder at the edge: zero slope at the edge, then a smooth, monotonic fade with exactly one inflection. `GlowProfile.cs` (Platform) is the reference, and `Glow.hlsl` mirrors it:*
     - `w = max(thickness px, 1 DIP) * (1 + 0.35 * Pulse)`: the shoulder. Thickness 0 still has a one-DIP shoulder, so the edge stays flat, and Thickness 0 and 1 draw the same light. A half-DIP floor would tell them apart, but at half render scale on the default Glow it would make Thickness 0 fall fastest at the outermost pixels again.
     - `glow = 0.85 * exp(-(sqrt(d * d + w * w) - w) / max(spread, 0.5))`, with `spread` as above. The hyperbola is about `d² / 2w` near the edge (flat) and about `d - w` further in (the exponential glow, `w` further in). The light never falls faster than the glow's own steepest, `0.85 / spread` per pixel; at `d = w` its relative rate of fall has eased up to 71 % of the exponential's.
     - The shoulder bends over about `w`, but the part that looks even grows more slowly: the light stays within 3 % of the edge for about `sqrt(0.06 * w * spread)` (at the default Glow on a 1080p screen, 4 px at Thickness 6 and 10 px at 40). When `w` is much larger than `spread` (a large Thickness on a narrow Glow), the shoulder is a soft half-Gaussian band with `sigma = sqrt(w * spread)`. A knee sharp enough to stay flat all the way to `w` would be a bar again.
     - `a = glow * Gain`, where `Gain = SoftClip(Intensity * (1 + 0.25 * Pulse))` is computed once a frame on the CPU. It is unchanged up to 0.9 and then rolls off toward 1 (0.95 at 1), so no pixel is clipped and the profile is only ever scaled. The beat widens the shoulder and the spread by the same factor, so it swells the whole light inward and never adds a rim.
     - One rounded distance field instead of step 1's square one, corner radius at least `2 * spread + w`. Outside the arc, in the screen's very corner, `d = 0`, and the arc now meets that plateau with zero slope instead of a kink.
     - At the defaults (Thickness 6, Glow 45 %) the light stays within 3 % of the edge for the first 4 px at 1080p and 100 % (round 1's band: 13 %), and the drop between neighbouring pixels never grows by more than one code value. Screens under 1080 px tall at 100 % have a narrower glow in pixels (Glow follows the short side) and the same 6 px shoulder: on 1280 × 720 the code values fall 3.6 % over the first 4 px (2.8 % in perceived brightness), and at Brightness 100 % the drop grows by up to 1.2 code values per pixel, a smooth bend with no step.
     - `SpreadPx` maps Glow with the slider to the power 1.5: the default 45 % is 38 px at 1080p.
     - The cbuffer above is the original sketch; `GlowConstants.cs` has the real layout (20 floats, 80 bytes, since the Line style and the notch).
   - *Clarification (Maxwell's request, H-032, with a photo of a MacBook whose screen edge has a crisp neon line hugging it and dipping round the camera notch, a dim glow inside it): "Please I want it to look like this or have an option to look like this and detect if theres a notch and go around it for the lighting". Round 2 took a thin bright rim out of the default look, so this is an option, `Settings.EdgeStyle = Line`, and the soft glow above stays the default (`Glow`). `GlowProfile.Alpha` is the reference per pixel; `Glow.hlsl` and the Mac's `glow_math.h` mirror it:*
     - **Line style** (`LinePx > 0`): `L = max(thickness px, 1 DIP)` is the line's width, so Thickness sets how wide the line is. `line = 1 - smoothstep(L - 0.75, L + 0.75, dLine)`: full brightness across the line, an anti-aliased inner edge 1.5 px wide, and a width the beat never changes. `dLine` is the depth through the screen's own shape: the top corners' radius (`TopCornerRadiusPx`) in the top half, the bottom corners' (`CornerRadiusPx`) below.
     - The halo inside it is the glow above with the narrowest shoulder (one DIP), the Glow setting's spread and the same beat push, at 0.5 instead of 0.85: `halo = 0.5 * exp(-(sqrt(d * d + w * w) - w) / spread)`, through the same rounded field.
     - They combine like light through two filters, `profile = 1 - (1 - line) * (1 - halo)`, and `a = profile * Gain`: the line is as bright as the gain allows, the beat brightens it through the gain, and the halo carries on past it.
     - **Notch** (both styles, `NotchHeightPx > 0`): a rounded box hanging from the top edge, from `NotchLeftPx` to `NotchRightPx` and down to `NotchHeightPx`, its lower corners rounded with `NotchRadiusPx = 0.3 * NotchHeightPx` (at most half its width). Every depth, the glow's field and `dLine`, becomes `max(smin(d, dNotch, NotchRadiusPx), 0)` with the polynomial smooth minimum `smin(a, b, k) = min(a, b) - h * h * k / 4`, `h = max(k - |a - b|, 0) / k`. Inside the notch the depth is 0; beside and below it, the distance from it; the inside corners where it meets the top edge are rounded like the notch's own "ears". The colour path stays on the outer rectangle, so the notch's outline takes the top edge's colours.
     - A notched screen rounds its top corners by about 10 points, so with a notch `TopCornerRadiusPx = max(CornerRadiusPx, 10 DIP)`. Windows screens have no notch: there `TopCornerRadiusPx = CornerRadiusPx` and the notch is all zeros.
     - With `LinePx = 0` and `NotchHeightPx = 0` (the Glow style on Windows) the shader branches over all of it and draws exactly the glow above.
3. **Perimeter coordinate** `t ∈ [0,1)`: the normalized arc-length position of the nearest edge point, going clockwise from the top-left. Compute it from which edge is nearest and the position along it. Approximating corners with the nearest edge is fine, as long as it is continuous.
4. **Color around the perimeter:** `u = frac(t + Phase)`. Build a smooth two-color loop where ColorA covers `Ratio` of the perimeter and ColorB covers the rest, with soft blends of width 0.08 at both boundaries (use `smoothstep`, wrap-safe). `rgb = lerp(ColorB, ColorA, wA)`.
5. **Mix in Oklab, not RGB.** RGB lerp between complementary colors goes muddy grey. Either precompute a 64-texel 1D gradient texture on the CPU each time the palette changes (preferred, cheap) and sample it with `u`, or do the Oklab conversion in-shader.
6. **Dither** to kill banding in the dark gradient tail: add interleaved-gradient noise of ±0.5/255 before output.
7. **Output premultiplied:** `return float4(rgb * a, a);`

Pixels with `a < 1/255` cost almost nothing. Fill rate at 4K/60 with this shader is fine on any iGPU from the last ~6 years. Profile anyway (Phase 3 acceptance).

## 4. Fullscreen / pause detection (Platform/System)
Re-evaluate on `EVENT_SYSTEM_FOREGROUND` and every 2 s:
- `SHQueryUserNotificationState`: `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_PRESENTATION_MODE`, or `QUNS_BUSY` → pause (when "Pause in fullscreen apps" is on).
- Also pause the overlay on a specific monitor if the foreground window exactly covers that monitor's rect and isn't the shell or desktop (`GetShellWindow`, class `Progman`/`WorkerW`). This catches borderless-fullscreen games and fullscreen video.
- Pausing fades out over 300 ms. Resuming fades in over 300 ms.

## 5. Demo mode (`--demo`)
Feed `LightEngine` a synthetic 120 BPM beat and cycle through 4 good-looking palettes, 6 s each. No audio capture needed. It's used to record the README GIFs (doc 08) and lets Maxwell check visuals without music.
