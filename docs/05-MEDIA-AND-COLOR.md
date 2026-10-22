# 05 — Now Playing & Album Colors

## 1. Now playing (Platform/Media)
Use `Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager` (GSMTC). It covers Spotify, Apple Music for Windows, Edge/Chrome/Firefox tabs (YouTube, SoundCloud…), foobar2000 with plugins, and most players.

- `RequestAsync()` once at startup, then subscribe to:
  - `CurrentSessionChanged` → re-bind to the new current session
  - session `MediaPropertiesChanged` → refetch properties
  - session `PlaybackInfoChanged` → track Playing/Paused
- `TryGetMediaPropertiesAsync()` → `Title`, `Artist`, `AlbumTitle`, `Thumbnail` (`IRandomAccessStreamReference`).
- Read the thumbnail with `OpenReadAsync()`, decode with `Windows.Graphics.Imaging.BitmapDecoder`, and **downscale while decoding** with `BitmapTransform` (ScaledWidth/Height = 64, Fant interpolation) to `Bgra8`/premultiplied-ignore. Pass raw pixels (`byte[]`, width, height) to Core.
- **Quirks to handle:**
  - `MediaPropertiesChanged` often fires 2–3 times per track change, sometimes with the thumbnail arriving late. Debounce 250 ms. If the thumbnail is null, retry once after 750 ms.
  - Some apps send a generic app icon as the thumbnail. If the extracted palette is nearly grayscale and the image is mostly a single flat color, treat it as "no art" and keep the previous palette or use the manual one. *Clarification (Maxwell's feedback): "mostly" caught black-and-white covers too. A black cover with white text is about 80 % one flat black, so it showed the manual colors instead of a neutral glow. "No art" is now an image that is essentially one flat color, with at least 98 % of its pixels within Oklab distance 0.03 of it (a blank thumbnail, or one with a speck), and a palette below chroma 0.05. A generic icon with a visible glyph can't be told from such a cover, so it now glows neutral too.* *Second clarification (Maxwell, on 1.0.0): an all-black cover, which some real albums have, was still "no art" and glowed the manual violet and light blue. "No art" is now only an image with no opaque pixel. Every other image is art: a blank or nearly blank one glows its own grey or color, since a player's blank placeholder can't be told from such a cover and a neutral glow is the smaller mistake.*
  - Browser sessions can switch rapidly when several tabs have media. Prefer the session whose PlaybackStatus is Playing. If several are playing, use `GetCurrentSession()`.
- Expose `NowPlaying(string? Title, string? Artist, string? SourceApp, bool IsPlaying)` for the tray tooltip ("Rimlight — Song · Artist").
- A track ID for change detection is `$"{SourceAppUserModelId}|{Artist}|{Title}"`.
- GSMTC needs no permission prompt. If `RequestAsync` throws (very old Windows or policy), log it and fall back to Manual colors permanently for the session.

## 2. Palette extraction (Core/Color)
Input: a 64×64 (or smaller) RGBA pixel array. Output: `Palette(Primary, Secondary)`.

Algorithm:
1. Convert each pixel to **Oklab**. Skip pixels with alpha < 128.
2. Run **k-means with k = 5** in Oklab (deterministic: seed with k-means++ using a fixed RNG seed; max 12 iterations). 4096 points × 5 clusters is trivially fast.
3. Score each cluster: `score = population^0.6 × (0.25 + chroma) × lightnessFitness`.
   - `chroma = sqrt(a² + b²)` in Oklab.
   - `lightnessFitness` penalizes near-black (L < 0.25) and near-white (L > 0.92): a smooth bump that's 1.0 in [0.4, 0.8].
4. **Primary** = the highest score.
5. **Secondary** = the highest score among clusters with Oklab distance ≥ 0.12 from Primary. If none qualifies, derive one by rotating Primary's hue by +35° in OkLCh and nudging L by +0.08.
6. **Glow-ify** both. The light should look like light, not paint, so in OkLCh:
   - raise chroma to at least 0.12 (unless the source cluster is truly grayscale with chroma < 0.03; then keep it neutral and the glow becomes soft white). *Clarification (Maxwell's feedback): a black-and-white cover with a slight cool cast (chroma 0.03–0.05) was raised to 0.12 and glowed light and dark blue. A color now keeps its chroma up to 0.05, so a grey or a tinted grey glows soft white or grey. It gets the full raise to 0.12 from chroma 0.10, and in between the raise eases in (a smoothstep), so there is no jump.* *Second clarification (Maxwell, on 1.0.0, Spotify on Windows): black-and-white covers still glowed "light blue and dark blue". A kept cast of chroma 0.02 looks grey on the cover, but lifted to the glow's lightness and spread around the screen as light it reads as a clear blue. So the palette is also judged as a whole: when both its colors are at or below chroma 0.045, the cover is black and white and both glow pure grey or white (chroma 0). From 0.06 on, the palette keeps its colors as above, and in between they fade in (a smoothstep). On 35 real covers, the black-and-white ones peak at 0.000–0.031, toned prints of black-and-white photos at 0.041–0.047, and muted color photos at 0.053 and up.*
   - clamp L to [0.55, 0.85]
   - gamut-map back to sRGB by reducing chroma until it fits
7. Return colors in **linear** RGB for the shader, and also sRGB hex for the UI.

## 3. Transitions (Core/Color)
- `PaletteBlender` crossfades from the current to the new palette over **800 ms**, interpolating in Oklab with ease-in-out. Recompute the 64-texel gradient texture each frame **only while a crossfade is active**.
- Manual color edits in Settings apply with a 200 ms crossfade, so the slider feels live but smooth.

## 4. Tests
- A solid red image → Primary ≈ red (hue within 10°). Secondary is a derived neighbor, not grey.
- A 70% blue / 30% orange image → Primary blue, Secondary orange.
- A black-and-white photo → both colors are near-neutral and glow as soft white, with no hallucinated hue.
- Deterministic: the same input gives the same output across runs.
- The near-black-heavy image (typical dark album cover with a small bright logo) still picks the bright logo color as Primary.
- Ship 6 small sample album-art-like PNGs in `tests/fixtures/` (generate them procedurally in the test project; don't use real album covers).
