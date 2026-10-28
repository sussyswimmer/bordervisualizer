"""Compares wav-analyze --engine replays with the oracle annotations.

Usage:
  sh run_harness.sh <corpus dir> <run dir> --movement Pulse        (once per build)
  python -I metrics.py <corpus dir> <run dir> [<run dir> ...] [--json out.json] [--md out.md]
Each run dir holds <id>.engine.csv per corpus track (run_harness.sh). Prints per-track and per-class tables.

The measures read the Pulse column, so they describe the Pulse movement, where the whole edge swells on each beat:
run the harness with --movement Pulse, as for the baselines in docs/PROGRESS.md (made before the Flow movement,
H-033). In Flow, the app's default, the whole-edge pulse is 40 % of the beat and the ripples carry the rest round the
edge, which the CSV leaves out: no pulse peak reaches a hit, and the size, depth and false-pulse measures shrink too.

Definitions (times are the CSV's frame times: when the frame is computed, display not included):
  pulse peaks       local maxima of the Pulse column (the last frame of a rise), with their height.
  hit               an oracle kick with a pulse peak >= 0.5 within [-20, +80] ms of its onset.
  latency (peak)    pulse peak time - kick onset, over hits.  latency (rise): the first frame in the window whose
                    Beat rose by more than 0.05 - kick onset, over hits.
  detected          an analyzer beat (the fired column) within [-20, +80] ms of a kick, whatever its grade; its
                    latency is the frame time of that beat - kick onset.
  pulse size        the largest pulse peak in a kick's window: mean and p10 over hits, and the mean over all kicks
                    with a miss counted as its largest peak or 0.
  bounce depth      over all kicks, how much brighter the glow gets: the largest "shown" value (below) from 20 ms
                    before to 150 ms after the kick over the shown value 30 ms before it, minus 1 (the 35 % push of
                    the glow's reach on a full pulse comes on top and isn't counted).
  false pulses      pulse peaks >= 0.3 with no kick onset within [peak - 80 ms, peak + 20 ms], per minute; "on onsets"
                    are those within 80 ms after a broadband (non-kick) onset of the oracle (voice, strums, snares).
  smoothness        Intensity resampled to a uniform 60 Hz grid: the share of its variance above 3 Hz (Welch), the
                    95th percentile of |dIntensity/dt| (per second), and pulse peaks >= 0.2 per minute. "Shown" is
                    what the overlay draws before Visibility in the Pulse movement: SoftClip(Intensity x (1 + 0.25 x
                    Pulse)) (Platform's GlowProfile.Gain, which in Flow also multiplies by 1 + 0.3 x the Flow
                    amount), with the same measures.
  proportionality   Pearson r of Intensity against the short-term loudness (3 s, LUFS) relative to the track's loud
                    level (its 95th percentile), and of each hit's pulse size against the momentary loudness (400 ms)
                    at the kick; mean Intensity in loud sections / in quiet ones (mid when a track has no quiet one);
                    pulse peaks >= 0.5 per minute inside quiet sections.
The first 2 s of every track are left out (the analyzer only listens there).
"""
import json, math, os, sys
import numpy as np

WARMUP = 2.0
WIN_LO, WIN_HI = -0.020, 0.080
HIT = 0.5


def load_csv(path):
    with open(path, encoding='utf-8') as f:
        header = f.readline().strip().split(',')
        rows = [line.rstrip('\n').split(',') for line in f]
    cols = {}
    for i, name in enumerate(header):
        vals = [r[i] for r in rows]
        cols[name] = np.array([float(v) if v != '' else np.nan for v in vals])
    return cols


def soft_clip(x):
    o = np.maximum(x - 0.9, 0) / 0.1
    return np.minimum(x, 0.9) + 0.1 * o / (1 + o)


def peaks_of(t, p):
    """Local maxima of p (scipy's find_peaks, plateaus at their first frame). Returns (times, heights)."""
    from scipy.signal import find_peaks
    idx, props = find_peaks(np.concatenate([[0.0], p, [0.0]]), height=1e-6, plateau_size=1)
    idx = props['left_edges'] - 1
    return t[idx], p[idx]


def hf_share(t, x, cutoff=3.0):
    """Share of the variance of x (resampled to 60 Hz) above cutoff Hz, by Welch's method."""
    from scipy.signal import welch
    grid = np.arange(t[0], t[-1], 1 / 60)
    if len(grid) < 512:
        return float('nan')
    xs = np.interp(grid, t, x)
    xs = xs - xs.mean()
    f, pxx = welch(xs, fs=60, nperseg=1024)
    total = pxx[1:].sum()
    return float(pxx[f > cutoff].sum() / total) if total > 0 else 0.0


def deriv_p95(t, x):
    grid = np.arange(t[0], t[-1], 1 / 60)
    xs = np.interp(grid, t, x)
    return float(np.percentile(np.abs(np.diff(xs)) * 60, 95))


def pearson(a, b):
    m = np.isfinite(a) & np.isfinite(b)
    if m.sum() < 10 or np.std(a[m]) == 0 or np.std(b[m]) == 0:
        return float('nan')
    return float(np.corrcoef(a[m], b[m])[0, 1])


def track_metrics(csv, oracle):
    t = csv['time']
    keep = t >= WARMUP
    pulse, beat, intensity = csv['pulse'], csv['beat'], csv['intensity']
    fired = csv['fired']
    shown = soft_clip(intensity * (1 + 0.25 * pulse))
    minutes = (t[-1] - WARMUP) / 60
    pt, ph = peaks_of(t, pulse)
    pk = pt >= WARMUP
    pt, ph = pt[pk], ph[pk]
    kicks = np.array([k['onset'] for k in oracle['kicks'] if k['onset'] >= WARMUP])
    beat_rise = np.concatenate([[False], np.diff(beat) > 0.05])
    fired_t = t[fired > 0]

    hits, sizes_all, lat_peak, lat_rise, lat_det, hit_sizes, hit_loud, depth_all = 0, [], [], [], [], [], [], []
    detected = 0
    lt = np.array(oracle['loudness']['times'])
    mom = np.array(oracle['loudness']['momentary_lufs'])
    st = np.array(oracle['loudness']['short_term_lufs'])
    loud_level = oracle['sections']['loud_level_lufs']
    for k in kicks:
        m = (pt >= k + WIN_LO) & (pt <= k + WIN_HI)
        best = ph[m].max() if m.any() else 0.0
        sizes_all.append(best)
        d = (fired_t >= k + WIN_LO) & (fired_t <= k + WIN_HI)
        if d.any():
            detected += 1
            lat_det.append(fired_t[d][0] - k)
        wk = (t >= k + WIN_LO) & (t <= k + 0.15)
        if wk.any():
            # How much brighter the glow gets on this kick: shown peak over the shown level just before the kick.
            base = np.interp(k - 0.03, t, shown)
            depth_all.append(shown[wk].max() / base - 1 if base > 0 else np.nan)
        if best >= HIT:
            hits += 1
            i = np.argmax(np.where(m, ph, -1))
            lat_peak.append(pt[i] - k)
            w = np.where((t >= k + WIN_LO) & (t <= k + WIN_HI) & beat_rise)[0]
            if w.size:
                lat_rise.append(t[w[0]] - k)
            hit_sizes.append(best)
            hit_loud.append(np.interp(k, lt, mom) - loud_level)

    # False pulses: peaks >= 0.3 without a kick onset within [peak - 80 ms, peak + 20 ms].
    big = ph >= 0.3
    false_t = []
    for tp in pt[big]:
        if kicks.size == 0 or not np.any((kicks >= tp - WIN_HI) & (kicks <= tp - WIN_LO)):
            false_t.append(tp)
    false_t = np.array(false_t)
    onsets = np.array([o['time'] for o in oracle['onsets']])
    on_onset = sum(1 for tp in false_t if onsets.size and np.any((onsets >= tp - WIN_HI) & (onsets <= tp - WIN_LO)))

    # Proportionality.
    st_rel = np.interp(t, lt, st) - loud_level
    r_int = pearson(intensity[keep & (t >= 3)], st_rel[keep & (t >= 3)])
    r_size = pearson(np.array(hit_sizes), np.array(hit_loud)) if len(hit_sizes) >= 10 else float('nan')
    runs = oracle['sections']['runs']

    def mean_in(label):
        m = np.zeros_like(t, dtype=bool)
        for r in runs:
            if r['label'] == label:
                m |= (t >= r['start']) & (t < r['end'])
        m &= keep
        return (float(intensity[m].mean()), float(m.sum() / 60)) if m.sum() > 60 else (float('nan'), 0.0)

    loud_i, _ = mean_in('loud')
    quiet_i, quiet_min = mean_in('quiet')
    mid_i, _ = mean_in('mid')
    low_i, low_label = (quiet_i, 'quiet') if math.isfinite(quiet_i) else (mid_i, 'mid')
    quiet_mask = np.zeros_like(pt, dtype=bool)
    for r in runs:
        if r['label'] == 'quiet':
            quiet_mask |= (pt >= r['start']) & (pt < r['end'])
    quiet_big = int(((ph >= HIT) & quiet_mask).sum())

    tk = t[keep]
    return dict(
        minutes=minutes, kicks=int(kicks.size), kicks_per_min=kicks.size / minutes if minutes > 0 else 0,
        hit_rate=hits / kicks.size if kicks.size else float('nan'), hits=hits,
        detected_rate=detected / kicks.size if kicks.size else float('nan'),
        lat_peak_med=float(np.median(lat_peak)) if lat_peak else float('nan'),
        lat_peak_p90=float(np.percentile(lat_peak, 90)) if lat_peak else float('nan'),
        lat_rise_med=float(np.median(lat_rise)) if lat_rise else float('nan'),
        lat_rise_p90=float(np.percentile(lat_rise, 90)) if lat_rise else float('nan'),
        lat_det_med=float(np.median(lat_det)) if lat_det else float('nan'),
        size_hit_mean=float(np.mean(hit_sizes)) if hit_sizes else float('nan'),
        size_hit_p10=float(np.percentile(hit_sizes, 10)) if hit_sizes else float('nan'),
        size_all_mean=float(np.mean(sizes_all)) if sizes_all else float('nan'),
        bounce_depth=float(np.nanmean(depth_all)) if depth_all else float('nan'),
        false_per_min=len(false_t) / minutes, false_on_onsets_per_min=on_onset / minutes,
        pulses02_per_min=float((ph >= 0.2).sum() / minutes), pulses05_per_min=float((ph >= 0.5).sum() / minutes),
        hf_intensity=hf_share(tk, intensity[keep]), hf_shown=hf_share(tk, shown[keep]),
        dI_p95=deriv_p95(tk, intensity[keep]), dShown_p95=deriv_p95(tk, shown[keep]),
        intensity_mean=float(intensity[keep].mean()), intensity_p10=float(np.percentile(intensity[keep], 10)),
        intensity_p90=float(np.percentile(intensity[keep], 90)),
        r_intensity_loudness=r_int, r_pulse_loudness=r_size,
        loud_over_low=loud_i / low_i if math.isfinite(loud_i) and math.isfinite(low_i) and low_i > 0 else float('nan'),
        low_label=low_label, quiet_minutes=quiet_min,
        quiet_big_pulses_per_min=quiet_big / quiet_min if quiet_min > 0.2 else float('nan'),
        # Internal stages where the build has them (6ac553b on): the mean punch over the track and at the kicks.
        punch_mean=float(np.nanmean(csv['punch'][keep])) if np.isfinite(csv['punch'][keep]).any() else float('nan'),
    )


def diagnose(csv, oracle):
    """Why kicks are missed or small, from the CSV's analyzer and internal columns.

    Missed detections (no analyzer beat within the window): blocked by the refractory time (a beat fired 20-180 ms
    before the kick), or the flux never crossed the threshold; for those, whether the median guard (10 x the median
    flux of the last second, BeatDetector.MedianOnsetRatio) was above mean + 1.5 sigma, i.e. set the threshold.
    The flux columns are per frame (the largest step), so the guard estimate is approximate.
    Detected kicks: the grade's factors from BeatCharacter's exposed values: kick-likeness, the punch before the
    onset, and accent x loud = grade / (kick-likeness x punch).
    """
    t, fired, flux, thr = csv['time'], csv['fired'], csv['flux'], csv['threshold']
    kicks = np.array([k['onset'] for k in oracle['kicks'] if k['onset'] >= WARMUP])
    fired_t = t[fired > 0]
    have_internals = np.isfinite(csv['grade']).any()
    refr = below = guard = 0
    ratios, kl, pu, al, gr = [], [], [], [], []
    for k in kicks:
        d = (fired_t >= k + WIN_LO) & (fired_t <= k + WIN_HI)
        if not d.any():
            if np.any((fired_t >= k - 0.18) & (fired_t < k + WIN_LO)):
                refr += 1
                continue
            w = (t >= k + WIN_LO) & (t <= k + WIN_HI)
            below += 1
            if w.any():
                i = np.argmax(np.where(w, flux, -1))
                ratios.append(flux[i] / thr[i] if thr[i] > 0 else np.nan)
                hist = (t >= t[i] - 1.0) & (t < t[i]) & (flux > 0)
                if hist.sum() > 10:
                    h = flux[hist]
                    if 10 * np.median(h) >= h.mean() + 1.5 * h.std():
                        guard += 1
            continue
        if not have_internals:
            continue
        f = np.where((t >= fired_t[d][0]) & (t <= fired_t[d][0] + 0.06))[0]
        before = np.where(t < fired_t[d][0])[0]
        if f.size == 0 or before.size == 0:
            continue
        j = f[np.argmax(csv['grade'][f])]
        g, kk, p = csv['grade'][j], csv['kickLike'][j], csv['punch'][before[-1]]
        gr.append(g)
        kl.append(kk)
        pu.append(p)
        al.append(g / (kk * p) if kk * p > 1e-3 else np.nan)
    n = max(1, kicks.size)
    med = lambda v: float(np.nanmedian(v)) if len(v) and np.isfinite(v).any() else float('nan')
    return dict(missed_refractory=refr / n, missed_below=below / n, missed_guard=guard / n,
                missed_flux_ratio=med(ratios), kick_like=med(kl), kick_like_p25=float(np.nanpercentile(kl, 25)) if kl else float('nan'),
                punch=med(pu), accent_loud=med(al), grade=med(gr),
                share_kick_like_below_05=float(np.mean(np.array(kl) < 0.5)) if kl else float('nan'),
                share_punch_below_05=float(np.mean(np.array(pu) < 0.5)) if pu else float('nan'))


def pooled(rows):
    """Class aggregates: kick-weighted rates and pooled latencies are approximated by kick-weighted means."""
    out = {}
    kicks = sum(r['kicks'] for r in rows)
    minutes = sum(r['minutes'] for r in rows)
    out['tracks'] = len(rows)
    out['kicks'] = kicks
    out['hit_rate'] = sum(r['hits'] for r in rows) / kicks if kicks else float('nan')
    out['detected_rate'] = sum(r['detected_rate'] * r['kicks'] for r in rows if r['kicks']) / kicks if kicks else float('nan')
    for key in ('lat_peak_med', 'lat_peak_p90', 'lat_rise_med', 'lat_det_med', 'size_hit_mean', 'size_hit_p10', 'size_all_mean', 'bounce_depth'):
        vals = [(r[key], r['kicks']) for r in rows if r['kicks'] and math.isfinite(r[key])]
        out[key] = sum(v * w for v, w in vals) / sum(w for _, w in vals) if vals else float('nan')
    for key in ('false_per_min', 'false_on_onsets_per_min', 'pulses02_per_min', 'pulses05_per_min'):
        out[key] = sum(r[key] * r['minutes'] for r in rows) / minutes
    for key in ('hf_intensity', 'hf_shown', 'dI_p95', 'dShown_p95', 'intensity_mean', 'r_intensity_loudness',
                'r_pulse_loudness', 'loud_over_low', 'quiet_big_pulses_per_min', 'punch_mean'):
        vals = [r[key] for r in rows if math.isfinite(r[key])]
        out[key] = float(np.mean(vals)) if vals else float('nan')
    return out


def fmt(x, spec='.2f'):
    return '–' if x is None or (isinstance(x, float) and not math.isfinite(x)) else format(x, spec)


def ms(x):
    return '–' if not math.isfinite(x) else f'{x * 1000:.0f}'


def main():
    args = sys.argv[1:]
    out_json = out_md = None
    if '--json' in args:
        i = args.index('--json'); out_json = args[i + 1]; del args[i:i + 2]
    if '--md' in args:
        i = args.index('--md'); out_md = args[i + 1]; del args[i:i + 2]
    corpus, runs = args[0], args[1:]
    manifest = json.load(open(os.path.join(corpus, 'manifest.json'), encoding='utf-8'))
    tracks = manifest['tracks']
    results = {}
    for run in runs:
        name = os.path.basename(os.path.normpath(run))
        results[name] = {}
        for tr in tracks:
            path = os.path.join(run, tr['id'] + '.engine.csv')
            if not os.path.exists(path):
                continue
            oracle = json.load(open(os.path.join(corpus, tr['id'] + '.oracle.json'), encoding='utf-8'))
            csv = load_csv(path)
            m = track_metrics(csv, oracle)
            m['class'] = tr['class']
            m['diagnosis'] = diagnose(csv, oracle)
            results[name][tr['id']] = m
    lines = []
    classes = ['calm', 'calm-then-drums', 'rap', 'edm', 'pop', 'piano']
    lines.append('### Kicks: hits, latency, size, false pulses (per track)\n')
    lines.append('| Track | Build | Kicks/min | Detected | Hit rate | Lat. peak med / p90 ms | Lat. rise med ms | Lat. beat med ms | Size hit mean / p10 | Size all kicks | Bounce depth | False ≥0.3 /min | …on onsets /min |')
    lines.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    for tr in tracks:
        for run in results:
            m = results[run].get(tr['id'])
            if m is None:
                continue
            lines.append(f"| {tr['id']} | {run} | {m['kicks_per_min']:.0f} | {fmt(m['detected_rate'] * 100, '.0f')} % | {fmt(m['hit_rate'] * 100, '.0f')} % | "
                         f"{ms(m['lat_peak_med'])} / {ms(m['lat_peak_p90'])} | {ms(m['lat_rise_med'])} | {ms(m['lat_det_med'])} | "
                         f"{fmt(m['size_hit_mean'])} / {fmt(m['size_hit_p10'])} | {fmt(m['size_all_mean'])} | {fmt(m['bounce_depth'] * 100, '.0f')} % | {m['false_per_min']:.1f} | {m['false_on_onsets_per_min']:.1f} |")
    lines.append('\n### Smoothness and proportionality (per track)\n')
    lines.append('| Track | Build | Pulses ≥0.2 /min | ≥0.5 /min | Intensity var. >3 Hz | Shown var. >3 Hz | p95 dI/dt /s | p95 dShown/dt /s | Intensity mean (p10–p90) | r(I, loudness) | r(pulse, loudness) | Loud / low sections | Big pulses /quiet min | Punch mean |')
    lines.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    for tr in tracks:
        for run in results:
            m = results[run].get(tr['id'])
            if m is None:
                continue
            lines.append(f"| {tr['id']} | {run} | {m['pulses02_per_min']:.1f} | {m['pulses05_per_min']:.1f} | {fmt(m['hf_intensity'] * 100, '.1f')} % | {fmt(m['hf_shown'] * 100, '.1f')} % | "
                         f"{fmt(m['dI_p95'])} | {fmt(m['dShown_p95'])} | {m['intensity_mean']:.2f} ({m['intensity_p10']:.2f}–{m['intensity_p90']:.2f}) | "
                         f"{fmt(m['r_intensity_loudness'])} | {fmt(m['r_pulse_loudness'])} | {fmt(m['loud_over_low'])} ({m['low_label']}) | "
                         f"{fmt(m['quiet_big_pulses_per_min'], '.1f')} | {fmt(m['punch_mean'])} |")
    lines.append('\n### Why kicks are missed or small (tracks with oracle kicks)\n')
    lines.append('Missed = no analyzer beat within [-20, +80] ms. "Refractory": a beat fired 20-180 ms before; "below threshold": '
                 'the flux never crossed it (with the largest flux / threshold in the window, and the share where the median '
                 'guard, 10 x the median flux of the last second, was above mean + 1.5 sigma). Detected kicks: medians of '
                 'BeatCharacter\'s kick-likeness, the punch before the onset, accent x loud (= grade / (kick-likeness x punch)) '
                 'and the grade; the shares of detected kicks with kick-likeness or punch under 0.5.\n')
    lines.append('| Track | Build | Missed: refractory | below threshold (flux/thr) | median guard set it | Kick-like med (p25) | Punch med | Accent x loud med | Grade med | Kick-like < 0.5 | Punch < 0.5 |')
    lines.append('|---|---|---|---|---|---|---|---|---|---|---|')
    for tr in tracks:
        for run in results:
            m = results[run].get(tr['id'])
            if m is None or m['kicks'] < 20:
                continue
            d = m['diagnosis']
            lines.append(f"| {tr['id']} | {run} | {d['missed_refractory'] * 100:.0f} % | {d['missed_below'] * 100:.0f} % ({fmt(d['missed_flux_ratio'])}) | "
                         f"{d['missed_guard'] * 100:.0f} % | {fmt(d['kick_like'])} ({fmt(d['kick_like_p25'])}) | {fmt(d['punch'])} | {fmt(d['accent_loud'])} | "
                         f"{fmt(d['grade'])} | {fmt(d['share_kick_like_below_05'] * 100, '.0f')} % | {fmt(d['share_punch_below_05'] * 100, '.0f')} % |")
    lines.append('\n### Per class\n')
    lines.append('| Class | Build | Kicks | Detected | Hit rate | Lat. peak med / p90 ms | Size hit mean / p10 | Size all kicks | Bounce depth | False ≥0.3 /min (on onsets) | Pulses ≥0.2 /min | Intensity var. >3 Hz | Shown var. >3 Hz | p95 dShown/dt | r(I, loudness) | Loud / low | Big pulses /quiet min |')
    lines.append('|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|')
    agg = {}
    for c in classes:
        for run in results:
            rows = [m for m in results[run].values() if m['class'] == c]
            if not rows:
                continue
            a = pooled(rows)
            agg.setdefault(run, {})[c] = a
            lines.append(f"| {c} | {run} | {a['kicks']} | {fmt(a['detected_rate'] * 100, '.0f')} % | {fmt(a['hit_rate'] * 100, '.0f')} % | {ms(a['lat_peak_med'])} / {ms(a['lat_peak_p90'])} | "
                         f"{fmt(a['size_hit_mean'])} / {fmt(a['size_hit_p10'])} | {fmt(a['size_all_mean'])} | {fmt(a['bounce_depth'] * 100, '.0f')} % | {a['false_per_min']:.1f} ({a['false_on_onsets_per_min']:.1f}) | {a['pulses02_per_min']:.1f} | "
                         f"{fmt(a['hf_intensity'] * 100, '.1f')} % | {fmt(a['hf_shown'] * 100, '.1f')} % | {fmt(a['dShown_p95'])} | {fmt(a['r_intensity_loudness'])} | {fmt(a['loud_over_low'])} | {fmt(a['quiet_big_pulses_per_min'], '.1f')} |")
    text = '\n'.join(lines)
    print(text)
    if out_md:
        with open(out_md, 'w', encoding='utf-8') as f:
            f.write(text + '\n')
    if out_json:
        with open(out_json, 'w', encoding='utf-8') as f:
            json.dump(dict(tracks=results, classes=agg), f, indent=1, default=float)


if __name__ == '__main__':
    main()
