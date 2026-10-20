"""Reference annotations for the Rimlight real-music corpus: an offline "oracle" that shares no code with Rimlight.

Usage: python -I oracle.py <corpus dir> [id ...]      (reads manifest.json, writes <id>.oracle.json per track)

Per track:
  kicks       kick onsets. librosa HPSS (margin 3) on an 11.025 kHz STFT (n_fft 1024, hop 32 = 2.9 ms); the percussive
              part summed over a low band (30-130 Hz; 30-100 Hz and stricter for calm and piano music); its log-level
              flux peak-picked. A candidate is a kick when, within 50 ms of it against the median of the 150 ms before:
                rise_db    the band's percussive level rises at least this much,
                tot_rise   the band's whole level (harmonic part included) rises at least 1.5 dB: a kick adds energy
                           on top of a sustained 808 or bass note, while the percussive part of a held 808 alone
                           flickers with no rise of the whole band (measured: kicks over an 808 2.4-4.4 dB, the
                           flicker -1.6 to +0.6 dB),
                floor_db   its band level is within this of the band's 95th-percentile level over the track,
                local_db   and, timed as below, its low-passed envelope peak is within this of the strongest kick's
                           within 3 s (a snare's or a ghost note's low end is about 20 dB under the kicks),
              and for calm, calm-then-drums and piano music also:
                low_share  the band carries at least this share of the rise of all percussive power (a strum or a
                           syllable puts almost all of its rise above 130 Hz: measured 0.15-0.46 for acoustic kicks,
                           under 0.05 for strums; not used for drum-led music, whose kicks often come layered with a
                           clap or a long pitch sweep and measure 0.04-0.10),
                sub        the 186 ms after it, against the 186 ms before (Hann, 5.4 Hz bins), rise at least sub_rise_db
                           in 30-65 Hz, with at least sub_share of the 30-130 Hz power there. A kick drum's fundamental
                           sits at 40-70 Hz; a guitar's lowest string is 82 Hz (73 Hz dropped) and its leakage below
                           65 Hz is about 30 dB down: measured, acoustic kicks 0.23-0.84 of the power in 30-65 Hz rising
                           20-25 dB, strums and plucks under 0.06,
                decay_db   and the low-passed envelope (below) 250 ms after its peak is at least this far under the
                           peak: a kick dies away within 100-200 ms, a guitar's or a piano's low note rings on (a
                           detuned guitar's 60 Hz notes passed the sub test and fell only about 5 dB in 250 ms).
              Timing, in the time domain: the 48 kHz signal low-passed (zero-phase 4th-order Butterworth at the band's
              top) and its Hilbert envelope; "peak" is the envelope's maximum 10 ms before to 90 ms after the
              candidate, "onset" where the envelope first rises 20 % of the way from its minimum in the 60 ms before
              the peak to the peak. STFT frames are centred, so the frame time alone leads the attack by up to half a
              window; this removes that bias.
  beats       librosa.beat.beat_track at its defaults (22.05 kHz, hop 512): tempo and beat times.
  loudness    ITU-R BS.1770 K-weighting (pyloudnorm's filters), momentary (400 ms) and short-term (3 s) LUFS every
              50 ms (the window ending at each time), and pyloudnorm's integrated loudness.
  onsets      broadband onsets (librosa.onset.onset_detect, backtracked), with those within 50 ms of a kick removed:
              vocal syllables, strums, snares, piano notes. A rough label: 'percussive' when the onset's rise is mostly
              in the percussive part above 150 Hz (snare, hats, hard strums), 'tonal' otherwise (voice, notes).
  sections    quiet / mid / loud from the short-term loudness against the track's loud level (its 95th percentile):
              loud within 4 dB of it, quiet 10 dB or more under it, mid between; runs under 3 s merged.
"""
import json, os, sys, warnings
import numpy as np
import librosa
import pyloudnorm
import scipy.signal as sg
import soundfile as sf

warnings.filterwarnings('ignore')

SR = 11025
N_FFT = 1024
HOP = 32
DRUMS = dict(band=(30, 130), rise_db=6.0, low_share=0.0, local_db=12.0, floor_db=20.0, spacing=0.09)
PARAMS = {
    # band Hz; minimum rise in dB; minimum share of the percussive rise in the band; within local_db of the strongest
    # kick within 3 s and floor_db of the band's 95th percentile; minimum spacing in s (16ths at 165 BPM are 91 ms).
    'rap': DRUMS,
    'edm': DRUMS,
    # Rock and pop: a bass guitar's note onsets and a snare's body also rise in the low band; the share and sub tests
    # keep the kick (a kit's kick and snare rarely land together, unlike EDM's layered kick and clap).
    'pop': dict(DRUMS, low_share=0.12, sub_rise_db=6.0, sub_share=0.1),
    'calm-then-drums': dict(DRUMS, band=(30, 120), rise_db=7.0, low_share=0.12, sub_rise_db=8.0, sub_share=0.1,
                            decay_db=8.0),
    # Calm and piano music: conservative, so a plucked low string or a low piano note isn't called a kick.
    'calm': dict(band=(30, 100), rise_db=9.0, low_share=0.15, local_db=10.0, floor_db=15.0, spacing=0.12,
                 sub_rise_db=10.0, sub_share=0.4, decay_db=10.0),
    'piano': dict(band=(30, 100), rise_db=9.0, low_share=0.15, local_db=10.0, floor_db=15.0, spacing=0.12,
                  sub_rise_db=10.0, sub_share=0.4, decay_db=10.0),
}
SUB_N = 2048        # 186 ms at 11.025 kHz
# ORACLE_DEBUG=t0:t1 prints every kick candidate's measures in that span (for checking the criteria).
DEBUG = tuple(float(x) for x in os.environ['ORACLE_DEBUG'].split(':')) if os.environ.get('ORACLE_DEBUG') else None
TOT_RISE_DB = 1.5


def sub_test(y, sr, t, p):
    """The 30-65 Hz rise and share right after t (see the module notes); (rise dB, share)."""
    i = int(t * sr)
    if i - SUB_N < 0 or i + SUB_N > len(y):
        return 0.0, 0.0
    w = np.hanning(SUB_N)
    after = np.abs(np.fft.rfft(y[i:i + SUB_N] * w)) ** 2
    before = np.abs(np.fft.rfft(y[i - SUB_N:i] * w)) ** 2
    f = np.fft.rfftfreq(SUB_N, 1 / sr)
    sub, up = (f >= 30) & (f <= 65), (f > 65) & (f <= 130)
    rise = 10 * np.log10((after[sub].sum() + 1e-12) / (before[sub].sum() + 1e-12))
    return float(rise), float(after[sub].sum() / (after[sub].sum() + after[up].sum() + 1e-12))
LOUD_DB, QUIET_DB = -4.0, -10.0


def kick_onsets(y, sr, y48, sr48, p):
    S = np.abs(librosa.stft(y, n_fft=N_FFT, hop_length=HOP))
    _, P = librosa.decompose.hpss(S, margin=3.0)
    freqs = librosa.fft_frequencies(sr=sr, n_fft=N_FFT)
    lo, hi = p['band']
    band = (freqs >= lo) & (freqs <= hi)
    pb = (P[band] ** 2).sum(0)
    pall = (P ** 2).sum(0)
    sb = (S[band] ** 2).sum(0)
    pdb = 10 * np.log10(pb + 1e-10)
    # Band flux: the positive change of the percussive band's log level over 3 frames (about 9 ms).
    flux = np.maximum(0, pdb - np.concatenate([np.full(3, pdb[0]), pdb[:-3]]))
    fps = sr / HOP
    peaks, _ = sg.find_peaks(flux, height=0.06 * np.percentile(flux, 99.5), distance=max(1, int(p['spacing'] * fps)))
    pre, post = int(0.15 * fps), int(0.05 * fps)
    floor = np.percentile(pdb, 95) - p['floor_db']
    cands = []
    for k in peaks:
        a, b = max(0, k - pre), min(len(pdb), k + post)
        if k <= a:
            continue
        rise = pdb[k:b].max() - np.median(pdb[a:k])
        dlow = pb[k:b].max() - np.median(pb[a:k])
        dall = pall[k:b].max() - np.median(pall[a:k])
        share = float(np.clip(dlow / dall, 0, 1)) if dall > 0 and dlow > 0 else 0.0
        level = pdb[k:b].max()
        tot_rise = 10 * np.log10((sb[k:b].max() + 1e-12) / (np.median(sb[a:k]) + 1e-12))
        if DEBUG and DEBUG[0] <= k / fps <= DEBUG[1]:
            sr_, ss_ = sub_test(y, sr, k / fps, p)
            print(f'  cand {k / fps:8.3f} rise {rise:5.1f} share {share:5.2f} level {level - floor - p["floor_db"]:6.1f} '
                  f'tot {tot_rise:5.1f} sub {sr_:5.1f}/{ss_:4.2f}')
        if rise >= p['rise_db'] and share >= p['low_share'] and level >= floor and tot_rise >= TOT_RISE_DB:
            if 'sub_share' in p:
                sub_rise, sub_share = sub_test(y, sr, k / fps, p)
                if sub_rise < p['sub_rise_db'] or sub_share < p['sub_share']:
                    continue
            cands.append((k, rise, share, level))
    times = librosa.frames_to_time(np.array([c[0] for c in cands], dtype=int), sr=sr, hop_length=HOP)
    # Time-domain timing on the 48 kHz signal.
    sos = sg.butter(4, hi, btype='lowpass', fs=sr48, output='sos')
    low = sg.sosfiltfilt(sos, y48)
    pad, smooth = int(0.45 * sr48), np.ones(int(0.001 * sr48)) / int(0.001 * sr48)
    out = []
    for (k, rise, share, level), t in zip(cands, times):
        c = int(t * sr48)
        a, b = max(0, c - pad), min(len(low), c + pad)
        env = np.convolve(np.abs(sg.hilbert(low[a:b])), smooth, mode='same')
        s0, s1 = c - a - int(0.01 * sr48), c - a + int(0.09 * sr48)
        if s0 < 0 or s1 > len(env):
            continue
        ip = s0 + int(np.argmax(env[s0:s1]))
        if 'decay_db' in p:
            d0, d1 = ip + int(0.24 * sr48), ip + int(0.26 * sr48)
            if DEBUG and DEBUG[0] <= t <= DEBUG[1] and d1 <= len(env):
                print(f'  decay {t:8.3f} {20 * np.log10(env[d0:d1].mean() / (env[ip] + 1e-12) + 1e-12):6.1f} dB')
            if d1 > len(env) or 20 * np.log10(env[d0:d1].mean() / (env[ip] + 1e-12) + 1e-12) > -p['decay_db']:
                continue
        b0 = max(0, ip - int(0.06 * sr48))
        ib = b0 + int(np.argmin(env[b0:ip + 1]))
        threshold = env[ib] + 0.2 * (env[ip] - env[ib])
        cross = ib + int(np.argmax(env[ib:ip + 1] >= threshold))
        out.append(dict(onset=round((a + cross) / sr48, 4), peak=round((a + ip) / sr48, 4), rise_db=round(float(rise), 2),
                        low_share=round(share, 3), level_db=round(float(level - np.percentile(pdb, 95)), 2),
                        peak_dbfs=round(float(20 * np.log10(env[ip] + 1e-12)), 2)))
    # Local test on the time-domain peaks: within local_db of the strongest within 3 s.
    peaks_db = np.array([o['peak_dbfs'] for o in out])
    onsets = np.array([o['onset'] for o in out])
    out = [o for o in out if o['peak_dbfs'] >= peaks_db[np.abs(onsets - o['onset']) <= 3.0].max() - p['local_db']]
    # Two candidates timed onto the same attack: keep the first.
    dedup = []
    for o in out:
        if dedup and o['onset'] - dedup[-1]['onset'] < 0.04:
            continue
        dedup.append(o)
    return dedup


def loudness(y48, sr48):
    meter = pyloudnorm.Meter(sr48)
    x = y48.astype(np.float64)
    for f in meter._filters.values():
        x = f.apply_filter(x)
    c = np.concatenate([[0.0], np.cumsum(x * x)])
    step = int(0.05 * sr48)
    ends = np.arange(step, len(x) + 1, step)

    def lufs(window):
        n = int(window * sr48)
        starts = np.maximum(0, ends - n)
        ms = (c[ends] - c[starts]) / np.maximum(1, ends - starts)
        return np.round(-0.691 + 10 * np.log10(ms + 1e-12), 2)

    return dict(step_seconds=0.05, times=np.round(ends / sr48, 3).tolist(), momentary_lufs=lufs(0.4).tolist(),
                short_term_lufs=lufs(3.0).tolist(), integrated_lufs=round(float(meter.integrated_loudness(y48)), 2))


def sections(times, st):
    st = np.asarray(st)
    t = np.asarray(times)
    valid = t >= 3.0          # the first 3 s window isn't full yet
    ref = float(np.percentile(st[valid], 95))
    rel = st - ref
    label = np.where(rel >= LOUD_DB, 'loud', np.where(rel <= QUIET_DB, 'quiet', 'mid'))
    runs = []
    for i in range(len(t)):
        if runs and runs[-1][2] == label[i]:
            runs[-1][1] = t[i]
        else:
            runs.append([t[i - 1] if i > 0 else 0.0, t[i], label[i]])
    merged = True
    while merged and len(runs) > 1:      # fold runs under 3 s into the longer neighbour
        merged = False
        for i, r in enumerate(runs):
            if r[1] - r[0] < 3.0:
                j = i - 1 if i > 0 and (i == len(runs) - 1 or runs[i - 1][1] - runs[i - 1][0] >= runs[i + 1][1] - runs[i + 1][0]) else i + 1
                lo, hi = min(i, j), max(i, j)
                keep = runs[j][2]
                runs[lo:hi + 1] = [[runs[lo][0], runs[hi][1], keep]]
                merged = True
                break
    out = []
    for r in runs:                        # join equal neighbours left by the folding
        if out and out[-1]['label'] == r[2]:
            out[-1]['end'] = round(float(r[1]), 2)
        else:
            out.append(dict(start=round(float(r[0]), 2), end=round(float(r[1]), 2), label=str(r[2])))
    return dict(loud_level_lufs=round(ref, 2), loud_within_db=-LOUD_DB, quiet_below_db=-QUIET_DB, runs=out)


def broadband_onsets(y, sr, kicks):
    S = np.abs(librosa.stft(y, n_fft=1024, hop_length=128))
    H, P = librosa.decompose.hpss(S, margin=2.0)
    freqs = librosa.fft_frequencies(sr=sr, n_fft=1024)
    env = librosa.onset.onset_strength(S=librosa.amplitude_to_db(S, ref=np.max), sr=sr, hop_length=128)
    frames = librosa.onset.onset_detect(onset_envelope=env, sr=sr, hop_length=128, backtrack=True, units='frames')
    times = librosa.frames_to_time(frames, sr=sr, hop_length=128)
    up = freqs >= 150
    kt = np.array([k['onset'] for k in kicks]) if kicks else np.array([])
    out = []
    for f, t in zip(frames, times):
        if kt.size and np.min(np.abs(kt - t)) <= 0.05:
            continue
        a, b = max(0, f - 3), min(S.shape[1], f + 6)
        dp = max(0.0, float(P[up, f:b].sum() - P[up, a:f + 1].sum()))
        dh = max(0.0, float(H[up, f:b].sum() - H[up, a:f + 1].sum()))
        out.append(dict(time=round(float(t), 3), type='percussive' if dp > 1.5 * dh else 'tonal'))
    return out


def main():
    corpus = sys.argv[1]
    only = set(sys.argv[2:])
    manifest = json.load(open(os.path.join(corpus, 'manifest.json'), encoding='utf-8'))
    for tr in manifest['tracks']:
        if only and tr['id'] not in only:
            continue
        p = PARAMS[tr['class']]
        y48, sr48 = sf.read(os.path.join(corpus, tr['wav']), dtype='float32')
        y = librosa.resample(y48, orig_sr=sr48, target_sr=SR, res_type='soxr_hq')
        kicks = kick_onsets(y, SR, y48, sr48, p)
        # librosa's defaults: 22.05 kHz, hop 512.
        y22 = librosa.resample(y48, orig_sr=sr48, target_sr=22050, res_type='soxr_hq')
        tempo, beats = librosa.beat.beat_track(y=y22, sr=22050, units='time')
        del y22
        tempo = float(np.atleast_1d(tempo)[0])
        loud = loudness(y48, sr48)
        secs = sections(loud['times'], loud['short_term_lufs'])
        onsets = broadband_onsets(y, SR, kicks)
        iki = np.diff([k['onset'] for k in kicks]) if len(kicks) > 1 else np.array([])
        doc = dict(id=tr['id'], class_=tr['class'], method=dict(
            sample_rate=SR, n_fft=N_FFT, hop=HOP, hpss_margin=3.0, kick=dict(p, tot_rise_db=TOT_RISE_DB), onset_rule='20% of the low-passed envelope rise',
            loudness='BS.1770 K-weighting (pyloudnorm filters), mean square over the window ending at each time',
            sections=f'loud >= {LOUD_DB} dB, quiet <= {QUIET_DB} dB relative to the 95th percentile of short-term LUFS'),
            duration=round(len(y48) / sr48, 3), tempo_bpm=round(tempo, 2), beats=np.round(beats, 3).tolist(),
            kicks=kicks, kick_count=len(kicks),
            kick_interval_median=round(float(np.median(iki)), 4) if iki.size else None,
            loudness=loud, sections=secs, onsets=onsets)
        doc['class'] = doc.pop('class_')
        with open(os.path.join(corpus, tr['id'] + '.oracle.json'), 'w', encoding='utf-8') as f:
            json.dump(doc, f)
        ikimed = f"{np.median(iki):.3f}" if iki.size else '-'
        print(f"{tr['id']:18s} {tr['class']:16s} kicks {len(kicks):4d} ({len(kicks) / doc['duration'] * 60:5.1f}/min) "
              f"tempo {tempo:6.1f} (beat {60 / tempo:.3f} s) median IKI {ikimed} onsets {len(onsets)} "
              f"I {loud['integrated_lufs']} LUFS sections {' '.join(r['label'][0] + str(int(r['end'] - r['start'])) for r in secs['runs'])}",
              flush=True)


if __name__ == '__main__':
    main()
