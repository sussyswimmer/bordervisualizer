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
