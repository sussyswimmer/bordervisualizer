# 10 — Native macOS app

Rimlight for Mac: the same glow, driven by the same Core, as a native menu bar app. This doc records the design decisions for the Mac app and the research behind them, then the milestones. Docs 01–06 still define the product; this doc only says how the Mac does it and where it differs. Product rules carry over unchanged: system audio only (the microphone is never opened or asked for), no telemetry, the update check is the only network call (but see §4.3 and Q4), an original product, and the name only from `src/Directory.Build.props` and `AppInfo.cs`.

Research date: **2026-10-19**. Each decision lists what was checked and where. **[Apple]** means Apple's documentation, **[MS]** Microsoft's, **[3P]** a reputable third party (a maintained GitHub project, Apple's developer forums with an Apple engineer, or the press), and **[unverified]** a claim from general knowledge that no source confirmed. Unverified items are tested on Maxwell's Mac in the milestone that needs them.

**Context at the time of writing:**
- macOS 27 shipped on 14 September 2026.
- macOS 26 Tahoe is the last release for Intel Macs.
- .NET 8 and .NET 9 both reach end of support on **10 November 2026**. .NET 10 (LTS) is supported until 14 November 2028, and .NET 11 is at RC2.

---

## 1. Stack, and how it reuses Core

### 1.1 Decision: a Swift/AppKit app over Core compiled with NativeAOT

```
┌──────────────────────────── Rimlight.app ─────────────────────────────┐
│ Swift (AppKit + SwiftUI): windows, Metal, audio tap, media, menu bar,   │
│ Settings, watchers, hotkey, login item, Sparkle                         │
│        │ C ABI (a dozen functions, blittable structs, no callbacks)     │
│        ▼                                                                │
│ libRimlightCore.dylib: NativeAOT, net10.0                               │
│   Rimlight.Native: the per-frame pipeline (a mirror of                  │
│   MusicGlowSource), GlowConstants, GlowProfile, FramePacer, settings    │
│   JSON, presets, palette extraction                                     │
│   └─ Rimlight.Core (unchanged, net8.0 project reference)               │
└─────────────────────────────────────────────────────────────────────────┘
```

- **Rimlight.Core is compiled as it is**, through a new `src/Rimlight.Native` class library that references it and exports a small C ABI with `[UnmanagedCallersOnly]`.
  - Publishing with `PublishAot` gives one self-contained `.dylib` for each architecture, with no .NET runtime to install.
  - The library also compiles the Windows app's Windows-free per-frame code as linked source files, unchanged: `GlowProfile.cs`, `GlowConstants.cs`, `FramePacer.cs`, `IOverlayFrameSource.cs` (for `FrameMotion`), `SrgbHex.cs` and `DemoBeat.cs`. These use only `Rimlight.Core` and `System`.
    - `DemoBeat.PaletteFade` reads `MusicGlowSource.AlbumFade`. M1 changes it to its own `TimeSpan.FromMilliseconds(800)`: the same value, so Windows behaves the same.
  - Its frame pipeline mirrors `MusicGlowSource`, and a test replays the same input through it and Bench's `FramePipeline` (§1.4).
  - **The analysis, the light engine, the palette, the edge profile and the frame pacing are therefore the same compiled C# on both platforms.**
- **Everything that talks to macOS is Swift**: windows, Metal, the Core Audio tap, now-playing, the menu bar, Settings (SwiftUI), the system watchers, the hotkey, the login item and Sparkle.

### 1.2 What was weighed

| | **A. C# app on .NET for macOS** (`net10.0-macos`, references Core) | **B. Swift/AppKit app + Core as a NativeAOT dylib** (chosen) |
|---|---|---|
| **Support window** | The macOS workload follows the MAUI policy: each major version is supported until **6 months after its successor ships**, not for the .NET LTS term. MAUI 10 ends **11 May 2027**, MAUI 8 (`net8.0-macos`) already ended in May 2025 [MS: dotnet.microsoft.com/platform/support/policy/maui]. That means a forced major upgrade every year. | NativeAOT for `osx-arm64`/`osx-x64` is part of the regular SDK, supported since .NET 8 [MS: learn.microsoft.com/dotnet/core/deploying/native-aot]. It follows .NET's own LTS dates: net10.0 is supported to Nov 2028. Swift and AppKit follow Apple. |
| **Building on CI** | Each workload release is built against **one exact Xcode**: the current .NET 10 workload needs Xcode 27.0, and the one before it Xcode 26.6 [MS: learn.microsoft.com/dotnet/ios/troubleshooting/xcode-requirement; github.com/dotnet/macios/releases]. The `macos-26` runner defaults to Xcode 26.6, and Xcode 27 is only on a preview image [3P: github.com/actions/runner-images]. So the workload, Xcode and runner have to be pinned together and moved together. | `swift build` works with whatever Xcode the runner has, and NativeAOT needs only the command line tools. No workload to install. |
| **The APIs this app needs** | AppKit, Metal, `CAMetalLayer`, `CADisplayLink`, ScreenCaptureKit and `SMAppService` are bound. **Core Audio process taps are not bound**: no `AudioHardwareCreateProcessTap`, `CATapDescription` or aggregate-device API, and the HAL `AudioObject*` calls are private P/Invokes [3P: dotnet/macios `main` at 27b2ccb, 16 Oct 2026]. The capture path (§2) would need hand-written Objective-C interop. | Every API is first-class, and Apple's tap sample is Swift [Apple: "Capturing system audio with Core Audio taps"]. |
| **No allocation per frame** | Core's DSP is allocation-free either way. But every Objective-C object a frame touches (command buffer, encoder, drawable) gets a new managed wrapper, so a C# render loop allocates GC objects every frame. | The C# side of a frame is the existing zero-allocation code, called once with pointers. Swift's per-frame Metal objects are reference-counted and freed in the frame's autorelease pool: the ordinary Metal pattern, with no GC. |
| **App size** | Tens of MB on CoreCLR. About 10–20 MB with NativeAOT, which is opt-in and was experimental in .NET 8 [3P: dotnet/macios docs/nativeaot.md; sizes **unverified**]. | A dylib of about 2–4 MB per architecture plus a small Swift binary and Sparkle [**unverified**, measured in M0]. |
| **Maintenance** | One language. But AppKit-from-C# has few samples, and every Apple sample has to be translated. | Two languages, split by a narrow seam: math in C# (tested on Linux and Mac), platform in Swift (the language Apple's docs and samples use). |

Option B wins on every point except "one language". The seam is small: the C ABI carries plain structs and pointers, and there are no callbacks from C# into Swift.

### 1.3 The C ABI (sketch; M1 fixes the details)

All functions are on `libRimlightCore`. The functions with a handle take the pipeline handle. Every struct is blittable and versioned by its size. Strings are UTF-8, written into a buffer the caller passes with its length, so no memory crosses the boundary to be freed.

- **Every export catches every exception** and returns a status code, logging the exception. An exception escaping an `[UnmanagedCallersOnly]` method ends the process.
- `Rimlight.Native` sets `IsAotCompatible`, so trimming and AOT warnings fail the build under `TreatWarningsAsErrors`.

| Function | Thread | What it does |
|---|---|---|
| `rl_app_name`, `rl_version` | any | `AppInfo.Name`, and the assembly's informational version, which comes from `<Version>`, so Swift never spells "Rimlight" |
| `rl_create(settingsDir)` / `rl_destroy` | main | the pipeline: analyzer, engine, blender, pacer, settings store |
| `rl_settings_load` / `rl_settings_save` (M0), `rl_settings_apply` (M1) | main | Settings cross as JSON with the settings file's field names (camelCase, enums as names). It's a transport only: Native has a source-generated context of its own over the public `Settings` record, because Core's JSON is internal. Fields the JSON leaves out keep their defaults: the source generator would otherwise set them to false or 0. Load and save go through Core's `ISettingsStore`, which validates the file. Apply builds a new immutable `Settings` and publishes it to the render thread (`Interlocked.Exchange`, doc 02). The Settings UI keeps every control within the ranges in CORE-API §7.1, and Core's consumers clamp what they read anyway |
| `rl_presets_*` | any | names and `Apply` |
| `rl_set_album_art(bgra, w, h, trackId)` | a background queue | `IPaletteExtractor.Extract`, then publishes the palette with `Volatile.Write` (doc 02) |
| `rl_set_demo`, `rl_set_paused`, `rl_set_audio_wanted` | any | the same switches as `MusicGlowSource` |
| `rl_frame(dt, samples*, count, sampleRate, out RlFrame*)` | render | one frame: analysis, light engine, gradient refill, the pacer's next `Pace` and interval. 0 B per frame |
| `rl_surface(widthPx, heightPx, pixelsPerPoint, out RlGlowConstants*)` | render | `GlowConstants.Create` for one screen; the layout is the shader's uniform block |
| `rl_gradient()` | render | a pointer to the 64 RGBA float texels the frame filled |

- **The audio thread never calls into .NET.** The Core Audio IO proc runs on a real-time thread, so it copies into a ring buffer written in C, in the Swift package. It has the same single-producer/single-consumer design as `AudioRingBuffer.cs`, using C11 atomics.
- The render thread drains the ring and passes the pointer to `rl_frame`. A real-time thread entering a garbage-collected runtime could be held by a GC suspension, which is why it stays out.

### 1.4 Testing the shared math

- `Rimlight.Native.Tests` (net10.0, xUnit) runs on the macOS job, and on Linux wherever a .NET 10 SDK is installed. It covers:
  - the C ABI round trips (settings JSON, presets, struct sizes);
  - a replay that feeds the same samples and settings through `rl_frame` and Bench's `FramePipeline` and requires identical `LightState`s;
  - a zero-allocation check on `rl_frame`.
  - These run the JIT build. A Swift test also links the published AOT dylib and calls `rl_create`, `rl_frame` and `rl_surface`, so the shipped binary is exercised too.
  - Bench and the linked Platform code each define a `FrameMotion`, so the tests import one through an alias.
- **The shader matches `GlowProfile` exactly.** The Metal shader is a line-by-line port of `Glow.hlsl`. Its math (rounded-box depth, eased distance, profile, perimeter coordinate) lives in `GlowMath.h`, which is valid both as Metal Shading Language and as C with `<simd/simd.h>`. Two checks use it:
  1. **CPU parity.** A C# test writes a golden table (`mac/Tests/Fixtures/glow-golden.json`): `GlowProfile` and `GlowConstants` for a grid of distances, sizes, thicknesses, glows, pulses and corner radii. A Swift test (`swift test`) compiles `GlowMath.h` as C and must match it within 1e-5.
     - A second C# test fails if the committed table is stale, so the profile can't change on one side alone.
     - `GlowMath.h` declares the uniform struct with `static_assert(sizeof(GlowConstants) == 64)`, mirroring `GlowConstants.SizeInBytes`.
  2. **GPU parity.** A Swift test renders the shader offscreen at known constants and compares the pixels with the same table (within one 8-bit code value, dither excluded).
     - It skips itself when no Metal device exists. Whether GitHub's arm64 macOS runners expose Metal is **unverified**, and M1 finds out. If they don't, it runs on Maxwell's Mac through `swift test`.

### 1.5 Layout and build

```
src/Rimlight.Native/          net10.0 class library, PublishAot, the C ABI (links the Windows-free Platform files)
src/Rimlight.Native.Tests/    xUnit, net10.0
mac/Package.swift             SwiftPM: RimlightMac (executable), RimlightC (C: ring buffer, GlowMath.h), tests
mac/Sources/…  mac/Tests/…
mac/Resources/                Info.plist template, Glow.metal, entitlements, icon
mac/build.sh                  AOT-publish both architectures + lipo → swift build (universal) → assemble Rimlight.app → sign → DMG
```

- **Ownership:** `src/Rimlight.Native*` and `mac/` are Lane B, Claude Code's (doc 09 §2 gets a line saying so in M0).
- **Neither new project goes into `Rimlight.sln` or `Rimlight.Core.slnf`**, so the Linux and Windows jobs build exactly what they build today.
  - `Rimlight.Core` stays `net8.0` and is referenced from net10.0, which .NET allows.
  - Moving Core itself to net10.0 before .NET 8's end of support is Codex's lane and Maxwell's call (HANDOFF H-029). It doesn't block the Mac.
- **SwiftPM plus a script, not an `.xcodeproj`.** A package is plain text that both agents can edit and review. The script assembles the bundle with `Info.plist`, `Contents/Frameworks/libRimlightCore.dylib`, the shader source and the icon, then signs it.
  - The dylib's install name is set to `@rpath/libRimlightCore.dylib` (`install_name_tool -id`), and the executable gets an `@executable_path/../Frameworks` rpath.
  - Signing goes inside out: the dylib first (and Sparkle's parts, M7), then the app. No `--deep`.
- **The name and version come only from the two sources.**
  - `build.sh` reads `<AppName>` and `<Version>` from `src/Directory.Build.props` into the `Info.plist` keys `CFBundleName`, `CFBundleExecutable` and `CFBundleShortVersionString`, the DMG name and the bundle ID `io.github.sussyswimmer.<appname-lowercase>`.
  - At runtime Swift reads the name from `rl_app_name()`, which is `AppInfo.Name`.
  - A rename stays one find-and-replace.
- **The shader is compiled at runtime from source** (`MTLDevice.makeLibrary(source:)`), as `Glow.hlsl` is on Windows. This avoids the Metal toolchain download, which Xcode 26 made a separate component [**unverified**]. The cost is a few milliseconds at startup.
  - Runtime compilation can't resolve `#include`, so `build.sh` joins `GlowMath.h` and `Glow.metal` into the single source file the bundle carries.

### 1.6 CI: a `macos` job in `ci.yml`

- **Runner:** `macos-26` (arm64, Xcode 26.6 by default) [3P: actions/runner-images].
- **Triggers:** `pull_request` and `workflow_dispatch`, so a manual run gives evidence.
- **Steps:**
  1. `actions/setup-dotnet` with 10.0.x.
  2. `dotnet test src/Rimlight.Native.Tests -c Release`.
  3. NativeAOT publish for osx-arm64 and osx-x64.
  4. `swift build -c release -Xswiftc -warnings-as-errors` and `swift test`.
  5. `mac/build.sh` for an ad-hoc-signed universal `Rimlight.app`, zipped with `ditto -c -k --keepParent` (so the binaries keep their permissions) and uploaded as an artifact. Maxwell can download and run that exact build. A downloaded build is quarantined, so his checklist includes "Open Anyway" (or `xattr -dr com.apple.quarantine Rimlight.app`).
- The C# projects keep `TreatWarningsAsErrors` from `Directory.Build.props`, so zero warnings is enforced on both sides.
- The `core` and `windows` jobs and `Rimlight.Core.slnf` stay unchanged. `.github/` is Lane A's (doc 09), so this one job is added with a note in HANDOFF (H-029), as the brief asks.

---

## 2. System audio capture

### 2.1 Decision: a Core Audio process tap (macOS 14.2+)

A global stereo tap that excludes Rimlight's own process, read through a private aggregate device, the same way as Apple's sample.

- **The API.**
  - `AudioHardwareCreateProcessTap` is macOS 14.2+ [Apple: developer.apple.com/documentation/coreaudio/audiohardwarecreateprocesstap(_:_:)].
  - `CATapDescription(stereoGlobalTapButExcludeProcesses:)` takes our own process's audio object [Apple: …/coreaudio/catapdescription].
  - Read the tap's UID with `kAudioTapPropertyUID`, create a private aggregate device whose tap list holds it, and read it with an IO proc "just like a microphone" [Apple: "Capturing system audio with Core Audio taps"; 3P: insidegui/AudioCap].
  - The tap leaves other apps audible: `muteBehavior` stays unmuted.
  - The aggregate device is private (`kAudioAggregateDeviceIsPrivateKey`), has the tap auto-start, and has the default output device as its main sub-device. Rimlight's own process object, which the tap excludes, comes from `kAudioHardwarePropertyTranslatePIDToProcessObject`.
- **The IO proc only converts to mono float and writes the C ring** (doc 02's rule). The render thread drains it.
  - The tap's format comes from the stream: usually 48 kHz float. Core gets the sample rate per frame as on Windows (`CoreFactory.AnalysisSampleRate`).
  - "No data for 100 ms means silence" carries over from doc 03 §1.
- **Permission: audio only, never the microphone.**
  - `Info.plist` must have `NSAudioCaptureUsageDescription` [Apple: …/nsaudiocaptureusagedescription]. Write it directly in the plist: one developer reports Xcode's build-setting route drops it [3P].
  - The prompt appears the first time IO starts on an aggregate device that holds a tap [Apple engineer, developer.apple.com/forums/thread/771864].
  - The setting lives under **Privacy & Security › Screen & System Audio Recording**, in the **"System Audio Recording Only"** list [3P, wording partly **unverified**]. No microphone permission is involved, and Rimlight never asks for one.
  - Our usage text: *"Rimlight listens to your Mac's sound output to light your screen to the beat. Nothing is recorded or saved."*
- **No public API checks or requests this permission** [Apple engineer, same thread]. Denied access gives buffers of exact zeros. So:
  - the first-run window explains the prompt **before** the tap starts, so the prompt has context;
  - after 10 s of exact digital zeros while another process is playing sound, Settings › Motion shows "Rimlight can't hear your Mac" with a button that opens that Settings pane.
    - "Playing" is the per-process `kAudioProcessPropertyIsRunningOutput` (macOS 14.2) on any process but our own. The device-wide `DeviceIsRunningSomewhere` isn't used: browsers and call apps keep the output open while sending silence, so it would misfire.
    - A quiet song is never exact zeros for 10 s, so this doesn't misfire on quiet music.
    - The private TCC preflight call that AudioCap uses is unreliable by its author's account [3P], so we don't use it.
- **Device changes.**
  - Watch `kAudioHardwarePropertyDefaultOutputDevice`, and rebuild the tap and aggregate device when it changes (AirPods connecting, a display's speakers), on wake, and when the stream format changes [3P: forum and developer reports].
  - Retry with doc 02's backoff (0.5 s → 5 s).
  - On each rebuild the analyzer is `Reset`, as on Windows.
- **No entitlement is needed for taps** outside the App Sandbox [3P]. Rimlight isn't sandboxed: Sparkle's normal setup, the window levels in §3 and Apple-event scripting are all simpler without it, and the app isn't on the Mac App Store.
- **Known issue:** a macOS 26.5 beta delivered zero buffers from a tap for long stretches [3P: forums thread 825780]. The rebuild above (on zeros while the system plays audio, at most once a minute) is the mitigation, and M2's checklist tests it.

### 2.2 Rejected: ScreenCaptureKit audio (macOS 13+)

- `SCStreamConfiguration.capturesAudio` and `excludesCurrentProcessAudio` work on macOS 13 [Apple]. But they need the full **Screen Recording** permission.
- Since macOS 15, that permission re-prompts **monthly** ("…wants to bypass the system private window picker") [3P: 9to5Mac, TidBITS].
- It always runs a video stream; the usual trick of a 2 × 2 display filter is **unverified**.
- A light that asks to record your screen every month is the wrong trade for a 14.2 floor that taps already give us. So macOS 13–14.1 aren't supported (Q1).

---

## 3. The overlay

### 3.1 The window: one per screen

The setup Apple DTS gave in May 2026, tested on macOS 26 [Apple DTS: developer.apple.com/forums/thread/826308]:

- **The app** is an accessory: `LSUIElement = YES` (no Dock icon). A regular app's windows can't sit over another app's full-screen Space.
- **The window** is an `NSPanel` with `.nonactivatingPanel` and `.borderless`, so showing it never activates Rimlight or pulls a full-screen app out of its Space.
  - `level = .screenSaver`. `.floating` and `.statusBar` sit **under** other apps' full-screen content.
  - `collectionBehavior = [.canJoinAllSpaces, .canJoinAllApplications, .fullScreenAuxiliary, .stationary, .ignoresCycle]`.
  - `ignoresMouseEvents = true` (click-through), `isOpaque = false`, `backgroundColor = .clear`, `hasShadow = false`.
  - `hidesOnDeactivate = false` (an NSPanel's default is true, which would hide the glow whenever another app is active), and `canBecomeKey` and `canBecomeMain` return false.
  - Shown with `orderFrontRegardless()`.
- **Frame:** `NSScreen.frame` when **Cover taskbar** is on (the Mac's label: "Cover the menu bar and Dock"), and `visibleFrame` when it's off. The settings field keeps its name, so the file format is the same (doc 06 §1).
  - `visibleFrame` changes when the Dock moves, resizes or auto-hides, and `didChangeScreenParameters` doesn't reliably report that. With Cover taskbar off, the overlay re-reads `visibleFrame` every 2 s along with the full-screen check (§5).
- **Hide from screen capture:** `sharingType = .none`. Users report that ScreenCaptureKit and full-screen captures still show such windows [3P: forums 808016], so the Settings text says "may not hide it from every recorder". The default stays off (PRD §5).
- **To test by hand [unverified]:** Mission Control, Stage Manager, App Exposé and moving between Spaces. No documented regressions were found for macOS 26 or 27.

### 3.2 Notch and rounded corners

- **The notch.**
  - `NSScreen.safeAreaInsets` and `auxiliaryTopLeftArea`/`auxiliaryTopRightArea` describe it [Apple: NSScreen].
  - The glow runs along the top edge as everywhere else. Under the notch it falls on no pixels, the way a display's bezel hides it, so there is nothing to draw around.
  - The menu bar is above the overlay only when Cover taskbar is off.
  - A notch-hugging outline is out of v1 (roadmap).
- **Rounded display corners.** NSScreen has **no public corner-radius API**; `_displayCornerRadius` is a private iOS selector [Apple's NSScreen symbol list; 3P].
  - Doesn't matter much here: the light's distance field already rounds its corners to at least two spreads plus the shoulder (`GlowProfile.GlowRadius`: about 80 px at the defaults on a 1080p screen, and more on a Retina one), well beyond a MacBook's corner. The hardware corner just crops a soft glow.
  - `CornerRadiusDip` stays the user's setting (default 0), as on Windows. No private API.

### 3.3 Drawing: Metal

- One `MTLDevice` and command queue for every screen.
- Each overlay's content view hosts a `CAMetalLayer`:
  - `pixelFormat = .bgra8Unorm` with an sRGB colour space;
  - `isOpaque = false`, so the composition is premultiplied;
  - `framebufferOnly = true`, `maximumDrawableCount = 2`, `displaySyncEnabled = true` [Apple: CAMetalLayer], and `allowsNextDrawableTimeout`, so a sleeping display can't hang the render thread.
- `drawableSize` = frame size × the screen's `backingScaleFactor` × render scale.
  - Half render scale (on battery with Reduce) draws at half size, and the layer stretches it.
  - Pixels per DIP are `backingScaleFactor × render scale`: one macOS point stands for the DIP, and both `GlowConstants.Create` and the Thickness and corner settings take it as their scale.
  - Thickness 6 is therefore 6 pt: 12 px on a Retina screen, the same pixel count as 6 DIP at 200 % on Windows.
- **`Glow.metal` is a line-by-line port of `Glow.hlsl`:** the same uniform block (`GlowConstants`, 64 bytes), a full-screen triangle from `vertex_id`, the 64-texel gradient as a 64 × 1 `rgba32Float` texture with linear filtering and repeat addressing, the same perimeter coordinate, mean-colour blend, sRGB encode and dither, and premultiplied output.
  - The profile functions sit in `GlowMath.h` and are tested against `GlowProfile` (§1.4).

### 3.4 Pacing: the display link, and nothing when static

- **`CVDisplayLink` is deprecated as of macOS 15** [Apple: CVDisplayLinkCreateWithCGDisplay availability].
- **The replacement is `NSScreen.displayLink(target:selector:)`** (macOS 14), which returns a `CADisplayLink` [Apple].
  - Use NSScreen's rather than a view's: per the docs, a view's link doesn't fire while the view is hidden [Apple: NSView.displayLink], and a hidden overlay still has to keep listening for music.
- **The link is added to a dedicated render thread's run loop**, not the main thread, so Settings or a busy menu never steps the glow. This thread owns Metal and calls `rl_frame` (doc 02).
- **One slow screen can't hold up the others.** `nextDrawable` can block for up to a second on a sleeping or slower screen. Each screen's layer therefore sets a short drawable timeout, and a screen without a free drawable skips that frame and reports it as not presented. `FramePacer`'s `AllPresented` and `Stalled` handle that as they do on Windows (a stalled screen keeps the slow rate, not the full one).
- **`FramePacer` is the policy**, compiled from the same C# file. It returns `Full`, `Slow` or `Block` after each frame, which maps to:

  | Pace | Mac |
  |---|---|
  | `Full` | The display link runs, with `preferredFrameRateRange` set to the FPS cap: 30, 60 or 120, or the screen's `maximumFramesPerSecond` for Native. ProMotion screens get 120 Hz only when the cap is 120 or Native. Default is 60, as on Windows [Apple: preferredFrameRateRange]. |
  | `Slow` | The link is paused, and a 100 ms run-loop timer drives frames (Idle Glow breathing, listening for music). |
  | `Block` | The link is paused and there's no timer. The thread sleeps in its run loop until an input change (settings, pause, battery, displays) signals a run-loop source. **0 % CPU.** |

  - Pacing follows the primary screen's refresh (`NSScreen.screens[0]`, the one with the menu bar; `NSScreen.main` is the key window's screen), as Windows follows the primary monitor's. The link is recreated when that screen changes.
  - Low Power Mode, thermal state and accessibility settings may lower the link's rate on their own [Apple], which is fine.
- **Mixed scaling.** Each screen gets its own `backingScaleFactor` and its own `GlowConstants`. `NSApplication.didChangeScreenParametersNotification` rebuilds the overlays, debounced by 300 ms as on Windows (doc 02). Displays are identified by `CGDisplayCreateUUIDFromDisplayID`, which stays stable across reboots and replugging, and those IDs are what `CustomMonitorIds` stores on the Mac.

---

## 4. Now playing and album art

### 4.1 What's ruled out

- **MediaRemote.framework (private)** stopped working for apps that load it directly in **macOS 15.4**: access is limited to processes with `com.apple.` bundle IDs [3P: ungive/mediaremote-adapter; Keyboard Maestro forum].
- **The known workaround, `mediaremote-adapter`**, runs `/usr/bin/perl` (which Apple signs) to load a helper framework, and streams JSON including artwork. It was last tested on macOS 26 in September 2026. Its own README warns that any update may break it [3P]. That makes it a loophole, and a fragile one. Not in v1 (Q5).
- **There's no public API for other apps' now-playing info.** `MPNowPlayingInfoCenter` only publishes your own [Apple].

### 4.2 Decision: Music and Spotify scripting, then the manual colours

1. **Triggers, with no permission.** Rimlight listens for the distributed notifications `com.apple.Music.playerInfo` and `com.spotify.client.PlaybackStateChanged`.
   - Both are undocumented. They carry the track name, artist, state and an ID, but no artwork [3P].
   - Listening needs no TCC permission [**unverified**; standard practice].
   - These give `NowPlaying` (title, artist, source app, playing) for the menu bar tooltip and the Color page, with a track ID of `app|artist|title`, as doc 05 §1.
2. **Artwork via Apple events, only on a track change, and only to a player that is already running.**
   - Rimlight checks `NSRunningApplication.runningApplications(withBundleIdentifier:)` first, because a scripting call launches a player that isn't running [**unverified**; standard practice].
   - **Music:** `raw data of artwork 1 of current track` gives the image file's bytes (JPEG or PNG), which Image I/O decodes [**unverified**; M4 tests it on macOS 26 and 27].
   - **Spotify:** `artwork url of current track` gives an HTTPS URL to Spotify's image server [3P]. Fetching that image is a network call (Q4).
   - **Permission:** an Automation prompt per player ("Rimlight wants to control Music"). It needs `NSAppleEventsUsageDescription` in the plist and the `com.apple.security.automation.apple-events` entitlement under the hardened runtime [Apple].
   - If the user declines, Rimlight falls back to the manual colours, and Settings › Color says why, with a button to the Automation pane.
3. **Decoding.** Image I/O makes a 64 × 64 thumbnail (`CGImageSourceCreateThumbnailAtIndex`), drawn into a BGRA8 bitmap and passed to `rl_set_album_art` on a background queue. Core's extractor, the "no art" check (doc 05 §1) and the 800 ms crossfade all apply unchanged.
4. **Every other player** (browsers, other apps) gets the manual colours, as when Windows finds no thumbnail. The beat sync doesn't depend on any of this, because it comes from the audio tap.

Doc 05's quirks carry over: debounce 250 ms, retry once after 750 ms when the art is missing, and prefer the player that is playing.

### 4.3 The network rule

- Apple Music's artwork comes from the local app.
- Spotify's comes only from the URL Spotify's own app hands over (`i.scdn.co`), fetched once per track change, with no cookies and a 64 px decode.
- That's a second network destination besides the update check, which is Maxwell's call (Q4). If he says no, Spotify plays with the manual colours.

---

## 5. The shell

- **Menu bar.** An `NSStatusItem` with a template icon from `assets/icon.svg`, dimmed when the glow is off. Its menu matches doc 06 §2: Glow on (with the hotkey), Mode, Colors, Presets, Settings…, Check for updates, Open logs folder, Quit, plus Demo mode.
  - **Difference from Windows:** a click opens the menu, as every Mac menu bar item does. Doc 06's left-click toggle is "Glow on" in the menu and the hotkey.
  - The tooltip text is the same: `Rimlight — Title · Artist`.
- **Settings.** One SwiftUI window (`NSHostingController`) with a sidebar holding **the same six pages** as doc 06 §3: Appearance, Color, Motion, Displays, Behavior, About.
  - Changes apply live, with a 500 ms save debounce. Closing the window hides it.
  - Appearance's live preview is a small `CAMetalLayer` that runs the same shader, so it can't drift from the overlay. Windows needed a CPU port (`GlowRaster`) instead.
  - It follows the system light and dark appearance, uses standard controls, and has VoiceOver labels on every control.
- **The settings file.** `~/Library/Application Support/Rimlight/settings.json` (the folder name is `rl_app_name()`), read and written by Core's own `ISettingsStore` through the C ABI. Same JSON, same validation, same backups. Logs go to `~/Library/Logs/Rimlight/` (5 × 1 MB, as on Windows).
  - Two fields read differently on the Mac:
    - `CustomMonitorIds` holds display UUIDs.
    - In `ToggleHotkey`, `Win` stands for ⌘. The default `Ctrl+Alt+L` is ⌃⌥L.
- **Global hotkey without Accessibility.**
  - Carbon's `RegisterEventHotKey` needs no Accessibility or Input Monitoring permission [3P: sindresorhus/KeyboardShortcuts, which wraps it].
  - `NSEvent` global monitors would need Accessibility, so they aren't used.
  - **macOS 15 rule:** hotkeys whose only modifiers are Option and/or Shift are refused (error −9868). This was eased for some apps in 15.2, with mixed reports since [Apple engineer, forums 763878; 3P]. So the Mac recorder requires ⌃ or ⌘, which the default already has.
  - A shortcut that fails to register shows the same inline warning as Windows.
- **Launch at login.** `SMAppService.mainApp.register()` (macOS 13) [Apple: SMAppService].
  - When the status is `.requiresApproval`, Behavior shows a button that opens Login Items (`openSystemSettingsLoginItems()`).
  - The first run turns it on (the `LaunchAtStartup` default), as on Windows.
- **Single instance.** An exclusive `flock` on `…/Application Support/Rimlight/instance.lock`; `NSRunningApplication` alone has a start-up race [3P]. A second launch posts a distributed notification (`<bundle id>.show-settings`) and exits, and the running copy opens Settings. `--demo` is forwarded the same way (the Mac has no debug visualizer yet, so there is no `--debug-visualizer` to forward). Opening the app from Finder while it runs starts no second process: LaunchServices sends the running copy a reopen event, which also opens Settings.
- **System watchers** (doc 02 lifetime events):

  | Event | Mac source | Action |
  |---|---|---|
  | Sleep and wake | `NSWorkspace.willSleepNotification` / `didWakeNotification` | Pause; on wake, rebuild the tap and the overlays |
  | Display off | `NSWorkspace.screensDidSleepNotification` / `screensDidWake…` | Pause |
  | Lock and unlock | Distributed `com.apple.screenIsLocked` / `…Unlocked` (undocumented, used by Chromium) [3P], plus `sessionDidResignActive` for fast user switching | Pause |
  | Displays | `didChangeScreenParametersNotification` | Rebuild, debounced 300 ms |
  | Battery | `IOPSNotificationCreateRunLoopSource` + `IOPSGetProvidingPowerSourceType` [**unverified**, long-standing IOKit API] | `OnBattery`: Normal, Reduce (30 fps, half scale) or Pause |
  | Low Power Mode | `ProcessInfo.isLowPowerModeEnabled` + `NSProcessInfoPowerStateDidChange` (macOS 12) [Apple] | Treated as on battery with Reduce |
  | Full-screen video or game | On app activation and every 2 s: `CGWindowListCopyWindowInfo` for the frontmost app's layer-0 windows. Bounds and owner PID need no permission; only window names need Screen Recording [Apple DTS, forums 839069] | Pause that screen when one of its windows exactly covers it (doc 04 §4's per-monitor pause) |

---

## 6. Distribution

- **The app and disk image.** `build.sh` makes a universal `Rimlight.app` and a DMG (`hdiutil`, with an Applications shortcut), named `RimlightSetup.dmg` to match the Windows download.
  - Both go on the same GitHub Release as the Windows assets, so the README's Mac button links to `releases/latest/download/RimlightSetup.dmg`.
- **Signing and notarization**, if a Developer ID is ever added. Maxwell has decided against an Apple Developer account for now (Q2), so the app is signed ad hoc and this stays ready but unused.
  - Hardened runtime, a secure timestamp, the Apple-events entitlement, then `notarytool submit --wait` and `stapler staple` on the app and the DMG [Apple: "Notarizing macOS software before distribution"].
  - The release workflow does it with secrets (certificate `.p12`, its password, and an App Store Connect API key), gated as Windows signing is.
- **Without a Developer ID**, two problems:
  1. **First launch.** macOS 15+ removed the Control-click › Open bypass. The user must try to open the app, then go to **System Settings › Privacy & Security › Open Anyway** and enter an admin password [3P: 9to5Mac; Wikipedia, Gatekeeper].
  2. **Permissions after updates.** An ad-hoc signature identifies the app by its exact hash. Every update is a "new app" to privacy settings, so the system-audio and Automation permissions are asked again after **every update** [**unverified** for the audio permission; this is how designated requirements work for ad-hoc code].
     - **Stopgap:** sign with a self-made certificate kept as a CI secret. Its designated requirement survives updates. It doesn't help with point 1.
- **Updates: Sparkle 2.** It's the standard Mac updater: an EdDSA-signed RSS appcast and delta updates, built by `generate_appcast` [3P: sparkle-project.org/documentation].
  - The appcast is a release asset, so the feed URL is `…/releases/latest/download/appcast.xml`. The check runs every 12 h and once at start-up after 30 s, and stays off when `AutoUpdate` is off (doc 06 §5). Sparkle's system-profile reporting stays off, so the update check is all that's sent.
  - **As built (M7):**
    - Sparkle 2.10.0 comes through SwiftPM, and `build.sh` copies `Sparkle.framework` into the app with Sparkle's own signature.
    - Ad-hoc signing is enough. Sparkle accepts an update when *either* its EdDSA signature or its code signature checks out (`SUUpdateValidator.m`), and the EdDSA signature always does.
    - The release workflow's `mac-appcast` job signs the update zip with `sign_update` (the private key comes on stdin), and `mac/appcast.sh` writes a one-item appcast. There are no delta updates, since `generate_appcast` would need the earlier releases' files.
    - A build without `SPARKLE_PUBLIC_KEY` (local and pull-request builds, or before Maxwell sets up the key: H-031) has no updater at all.
    - The schedule is Sparkle's: every 12 h (`SUScheduledCheckInterval`), counted from the last check. That's close to, but not exactly, Windows' "30 s after start, then every 12 h".
    - With AutoUpdate on, the update downloads in the background and installs when the app quits, as Velopack applies on restart. With it off, only **Check for Updates…** checks.
    - **Known limit:** `sparkle:version` is the version without its prerelease part, so a copy on 1.1.0-beta.1 sees 1.1.0 as the same version and is only offered 1.1.1. Prereleases are never in the feed.
  - **Velopack was considered** because Windows uses it. It supports macOS `.app` bundles, signing, notarization and deltas, but makes a `.pkg` rather than a DMG, and its updater would need its C# SDK inside the NativeAOT library [3P: docs.velopack.io]. Sparkle is the Mac convention, and the Windows pipeline stays as it is.
- **Architectures:** a universal binary (arm64 + x86_64) by default (Q3).
  - NativeAOT publishes each architecture and `lipo` joins them. Swift builds universal with `--arch arm64 --arch x86_64`.
  - CI builds both and runs the arm64 tests. An Intel smoke test can use the `macos-26-intel` runner [3P: runner-images].
- **Minimum macOS: 14.4** (Q1). The floor is set by the process tap (14.2 [Apple]; 14.4 is the floor AudioCap tests on [3P]) and `NSScreen.displayLink` (14.0).

---

## 7. Milestones

One milestone is one branch (`claude/mac-<m>-<slug>`), one PR into PR #32's branch while that is open and into `main` once it has merged, Conventional Commits, an independent review before the PR, and a manual test checklist for Maxwell when something visible changes.

| | Milestone | Done when |
|---|---|---|
| **M0** | Skeleton: `Rimlight.Native` (C ABI: name, version, settings round trip) + its tests, the Swift package, `build.sh`, the `macos` CI job | CI builds a universal, ad-hoc-signed `Rimlight.app` artifact with zero warnings. Maxwell runs it and sees a menu bar icon with Quit; the name and version come from `rl_*` |
| **M1** | The glow on every display with the demo beat (`--demo`): overlay panels, Metal, `Glow.metal`, `GlowMath.h` parity tests, render thread, display link, FramePacer | The demo glows on every screen, over full-screen apps, click-through, correct on mixed Retina and non-Retina screens. Idle CPU ≈ 0 % |
| **M2** | Core Audio tap → C ring → `rl_frame` → glow; device changes; the "can't hear" check | Beat-synced glow from Music, Spotify and a browser; survives AirPods connecting. Under 2 % CPU while playing |
| **M3** | Menu bar menu, Settings (six pages, live preview), the shared settings file, first-run window | Every setting applies live and survives a restart |
| **M4** | Now playing and album art (Music, Spotify) | Colours crossfade on a track change; manual colours otherwise |
| **M5** | System watchers (table in §5) | Sleep/wake, lock, display changes, battery and full-screen video behave as on Windows |
| **M6** | Hotkey, login item, single instance | ⌃⌥L toggles; starts at login; a second launch opens Settings |
| **M7** | DMG, signing and notarization (per Q2), Sparkle, the release workflow step | A tagged release carries a DMG that installs and updates itself |
| **M8** | README Mac section and docs, under doc 08's rules | Links and badges checked; Mac recording steps for Maxwell |

---

## 8. Decisions for Maxwell

They're also HANDOFF H-030. **Maxwell's answer (2026-10-19): "Just build it, no app developer."** So Q0 is yes (Swift) and Q2 is no (ad-hoc signing; §6 "Without a Developer ID" applies). Q1, Q3 and Q5 follow the recommendation. Q4 stays **No** (the one-network-call rule) until he says yes.

| | Decision | Recommended |
|---|---|---|
| **Q0** | Build the Mac app in **Swift**, over the shared C# Core (§1). CLAUDE.md asks before a stack change. | **Yes** (Maxwell: yes) |
| **Q1** | Minimum macOS | **14.4 Sonoma.** System-audio taps need 14.2, and 14.4 is the tested floor. Covers every Mac that runs Sonoma or later. |
| **Q2** | An Apple Developer account ($99 a year) for signing and notarization | **Yes, before the first public Mac release.** Without it users must click "Open Anyway" in System Settings, and macOS asks for the audio permission again after every update. M0–M6 don't need it. (Maxwell: no account.) |
| **Q3** | Universal (Apple silicon + Intel) or Apple silicon only | **Universal.** Costs a few MB and a CI step. But no Intel Mac will test it unless you have one, and Intel Macs stop at macOS 26. |
| **Q4** | Fetch Spotify's album art from Spotify's image server: a second network call, stated in the README's privacy section | **Yes**, only on a track change, only Spotify's own URL. Until you say yes, Spotify gets the manual colours. |
| **Q5** | Album colours from browsers and other players through the `mediaremote-adapter` loophole | **No for v1.** It's a private-API workaround Apple can close in any update. Revisit later. |
