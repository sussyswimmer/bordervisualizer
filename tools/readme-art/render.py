"""Renders the README pictures with Rimlight's own glow math, driven by its real beat engine.

Usage:
  python3 tools/readme-art/render.py <groove.engine.csv> <assets dir>

The CSV comes from `wav-analyze --engine` on a synthetic track (README.md in this folder). The glow is Glow.hlsl
and GlowProfile.cs, ported line for line; the colors are PaletteBlender's Oklab gradient. Needs numpy, Pillow and
ffmpeg.
"""
import csv, os, subprocess, sys, tempfile
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W, H = 1920, 1080          # the screen being rendered (a 1080p monitor at 100 %)
GLOW_PEAK, BEAT_PUSH, BEAT_GAIN, KNEE = 0.85, 0.35, 0.25, 0.9
BLEND_WIDTH = 0.08


def srgb_to_linear(c):
    c = np.asarray(c, dtype=np.float64)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c):
    c = np.clip(c, 0, 1)
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1 / 2.4) - 0.055)


def hex_linear(h):
    return srgb_to_linear([int(h[i:i + 2], 16) / 255 for i in (1, 3, 5)])


def to_oklab(rgb):
    r, g, b = rgb
    l = np.cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b)
    m = np.cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b)
    s = np.cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b)
    return np.array([0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
                     1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
                     0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s])


def from_oklab(lab):
    L, a, b = lab
    l = (L + 0.3963377774 * a + 0.2158037573 * b) ** 3
    m = (L - 0.1055613458 * a - 0.0638541728 * b) ** 3
    s = (L - 0.0894841775 * a - 1.2914855480 * b) ** 3
    return np.clip(np.array([4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
                             -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
                             -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s]), 0, 1)


def mix(c1, c2, t):
    """Oklab blend of two linear colors, as PaletteBlender's crossfade."""
    return from_oklab(to_oklab(c1) * (1 - t) + to_oklab(c2) * t)


def smoothstep(x):
    x = np.clip(x, 0, 1)
    return x * x * (3 - 2 * x)


def gradient(primary, secondary, ratio):
    """PaletteBlender.FillGradient: 64 linear texels around the perimeter."""
    u = (np.arange(64) + 0.5) / 64
    offset = u - ratio / 2
    offset -= np.floor(offset + 0.5)
    weight = smoothstep((ratio / 2 - np.abs(offset) + BLEND_WIDTH / 2) / BLEND_WIDTH)
    pl, sl = to_oklab(primary), to_oklab(secondary)
    return np.array([from_oklab(sl * (1 - w) + pl * w) for w in weight])


class Screen:
    """The per-pixel fields of Glow.hlsl that don't change from frame to frame for one look."""

    def __init__(self, thickness_dip, glow):
        short = min(W, H)
        self.spread = max(1.0, glow ** 1.5 * 0.35 / 3 * short)          # GlowProfile.SpreadFor
        self.shoulder = max(thickness_dip, 1.0)                          # GlowProfile.ShoulderFor
        glow_radius = min(max(0, 2 * self.spread + self.shoulder), short / 2)
        color_radius = min(max(6 * self.spread, 2 * self.shoulder), short / 2)
        y, x = np.mgrid[0:H, 0:W].astype(np.float64) + 0.5
        qx = np.abs(x - W / 2) - W / 2 + glow_radius
        qy = np.abs(y - H / 2) - H / 2 + glow_radius
        outside = np.hypot(np.maximum(qx, 0), np.maximum(qy, 0))
        self.depth = np.maximum(-(outside + np.minimum(np.maximum(qx, qy), 0) - glow_radius), 0)
        self.u = perimeter_t(x, y, color_radius)
        edge = np.minimum(np.minimum(x, y), np.minimum(W - x, H - y))
        self.to_mean = smoothstep((edge - 0.5 * color_radius) / (0.5 * color_radius))[..., None]

    def light(self, texels, intensity, pulse, phase, visibility=1.0):
        """Premultiplied sRGB color and alpha of the glow, Glow.hlsl steps 2-7."""
        spread = max(self.spread * (1 + BEAT_PUSH * pulse), 0.5)
        shoulder = self.shoulder * (1 + BEAT_PUSH * pulse)
        x = intensity * (1 + BEAT_GAIN * pulse)
        over = max(x - KNEE, 0) / (1 - KNEE)
        gain = min(x, KNEE) + (1 - KNEE) * over / (1 + over)             # GlowProfile.SoftClip
        a = GLOW_PEAK * np.exp(-(np.sqrt(self.depth ** 2 + shoulder ** 2) - shoulder) / spread) * gain * visibility
        pos = ((self.u + phase) % 1.0) * 64 - 0.5
        i0 = np.floor(pos).astype(int) % 64
        f = (pos - np.floor(pos))[..., None]
        rgb = texels[i0] * (1 - f) + texels[(i0 + 1) % 64] * f
        rgb = rgb * (1 - self.to_mean) + texels.mean(axis=0) * self.to_mean
        return linear_to_srgb(rgb) * a[..., None], a[..., None]


def perimeter_t(x, y, r):
    """Glow.hlsl PerimeterT, vectorized."""
    top_len, side_len, arc = W - 2 * r, H - 2 * r, np.pi / 2 * r
    total = 2 * (top_len + side_len) + 4 * arc
    q = lambda yy, xx: np.arctan2(yy, np.maximum(xx, 1e-5)) / (np.pi / 2)
    d_top, d_right, d_bottom, d_left = y, W - x, H - y, x
    nearest = np.minimum(np.minimum(d_top, d_right), np.minimum(d_bottom, d_left))
    s = np.where(nearest == d_top, x - r,
        np.where(nearest == d_right, top_len + arc + (y - r),
        np.where(nearest == d_bottom, top_len + 2 * arc + side_len + (W - r - x),
                 2 * top_len + 3 * arc + side_len + (H - r - y))))
    left, right, top, bottom = x < r, x > W - r, y < r, y > H - r
    s = np.where(top & right, top_len + arc * q(x - (W - r), -(y - r)), s)
    s = np.where(bottom & right, top_len + arc + side_len + arc * q(y - (H - r), x - (W - r)), s)
    s = np.where(bottom & left, 2 * top_len + 2 * arc + side_len + arc * q(-(x - r), y - (H - r)), s)
    s = np.where(top & left, 2 * top_len + 3 * arc + 2 * side_len + arc * q(-(y - r), -(x - r)), s)
    return ((s + 0.5 * arc) / total) % 1.0


_wallpaper = None


def wallpaper():
    """A plain dark desktop: a deep blue-violet gradient with a soft light in the middle."""
    global _wallpaper
    if _wallpaper is not None:
        return _wallpaper
    y, x = np.mgrid[0:H, 0:W] / np.array([H, W])[:, None, None]
    top, bottom = np.array([0.055, 0.060, 0.105]), np.array([0.020, 0.022, 0.040])
    img = top * (1 - y[..., None]) + bottom * y[..., None]
    glow = np.exp(-(((x - 0.5) * 1.6) ** 2 + ((y - 0.45) * 2.2) ** 2) * 2.5)[..., None]
    _wallpaper = np.clip(img + glow * np.array([0.030, 0.028, 0.050]), 0, 1)
    return _wallpaper


def cover(primary, secondary, size=300):
    """An abstract album cover in the palette's colors, centred on the screen with a soft shadow (RGBA)."""
    pad = 60
    img = Image.new('RGBA', (size + 2 * pad, size + 2 * pad), (0, 0, 0, 0))
    shadow = Image.new('L', img.size, 0)
    ImageDraw.Draw(shadow).rounded_rectangle([pad, pad + 14, pad + size, pad + size + 14], 26, fill=150)
    img.putalpha(shadow.filter(ImageFilter.GaussianBlur(24)))
    y, x = np.mgrid[0:size, 0:size] / size
    t = np.clip(0.55 * x + 0.45 * y, 0, 1)[..., None]
    p, s = linear_to_srgb(primary), linear_to_srgb(secondary)
    art = p * (1 - t) + s * t
    ring = np.exp(-((np.hypot(x - 0.62, y - 0.38) - 0.22) / 0.035) ** 2)[..., None]
    art = np.clip(art * 0.82 + ring * 0.35, 0, 1)
    face = Image.fromarray((art * 255).astype(np.uint8), 'RGB').convert('RGBA')
    mask = Image.new('L', (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], 26, fill=255)
    img.paste(face, (pad, pad), mask)
    return img


def compose(screen, texels, light, primary=None, secondary=None):
    base = wallpaper()
    if primary is not None:
        bg = Image.fromarray((base * 255).astype(np.uint8), 'RGB').convert('RGBA')
        c = cover(primary, secondary)
        bg.alpha_composite(c, ((W - c.width) // 2, (H - c.height) // 2))
        base = np.asarray(bg.convert('RGB'), dtype=np.float64) / 255
    color, alpha = screen.light(texels, *light)
    return color + base * (1 - alpha)                                  # DWM's blend, in sRGB-encoded space


def frame_png(rgb, size):
    img = Image.fromarray((np.clip(rgb, 0, 1) * 255 + 0.5).astype(np.uint8), 'RGB')
    return img.resize(size, Image.LANCZOS)


def main():
    engine_csv, out = sys.argv[1], sys.argv[2]
    rows = list(csv.DictReader(open(engine_csv)))
    times = np.array([float(r['time']) for r in rows])

    # The animation: 8 s of a 120 BPM groove from the engine (after it has found the groove), with one album change
    # there and back, so the loop is seamless: 16 beats, and the colours end where they start.
    screen = Screen(6, 0.45)
    a = (hex_linear('#7C5CFF'), hex_linear('#22D3EE'))
    b = (hex_linear('#FF8A2A'), hex_linear('#FF4F9A'))
    fps, seconds, start = 20, 8.0, 8.0
    with tempfile.TemporaryDirectory() as tmp:
        for k in range(int(fps * seconds)):
            t = k / fps
            row = rows[int(np.argmin(np.abs(times - (start + t))))]
            fade = smoothstep((t - 3.2) / 0.8) - smoothstep((t - 7.2) / 0.8)
            primary, secondary = mix(a[0], b[0], fade), mix(a[1], b[1], fade)
            texels = gradient(primary, secondary, 0.6)
            # The live glow's colours also creep round the screen; here they sway, so the loop closes.
            phase = 0.24 + 0.03 * np.sin(2 * np.pi * t / seconds)
            light = (float(row['intensity']), float(row['pulse']), phase, float(row['visibility']))
            frame_png(compose(screen, texels, light, primary, secondary), (960, 540)).save(f'{tmp}/f{k:04d}.png')
        palette = f'{tmp}/palette.png'
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-framerate', str(fps), '-i', f'{tmp}/f%04d.png',
                        '-vf', 'palettegen=max_colors=256:stats_mode=diff', palette], check=True)
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-framerate', str(fps), '-i', f'{tmp}/f%04d.png', '-i', palette,
                        '-lavfi', 'paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle', '-loop', '0', os.path.join(out, 'preview.gif')],
                       check=True)

    # The four looks, each at a beat.
    looks = {
        'aurora': ('#14E3C5', '#9D4DFF', 0.60, 6, 0.60, 0.85),
        'sunset': ('#FF8A2A', '#FF4F9A', 0.55, 6, 0.50, 0.85),
        'neon': ('#FF2BD6', '#00E5FF', 0.50, 12, 0.35, 1.00),
        'ember': ('#FF3A1F', '#FFB01F', 0.65, 4, 0.80, 0.60),
    }
    for name, (p, s, ratio, thickness, glow, brightness) in looks.items():
        look = Screen(thickness, glow)
        texels = gradient(hex_linear(p), hex_linear(s), ratio)
        intensity = brightness * (0.35 + 0.65 * 0.85)                   # LightEngine at a loud moment
        rgb = compose(look, texels, (intensity, 0.5, 0.1), hex_linear(p), hex_linear(s))
        frame_png(rgb, (800, 450)).save(os.path.join(out, f'look-{name}.png'), optimize=True)


if __name__ == '__main__':
    main()
