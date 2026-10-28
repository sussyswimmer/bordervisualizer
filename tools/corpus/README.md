# Real-music corpus

Maxwell's round-2 feedback showed that tuning on synthetic tracks isn't enough, so round 2 tuned the dynamics on these 15 Creative Commons recordings. This folder holds everything needed to rebuild that test bed and measure a build on it. It holds no audio: `fetch.py` downloads the tracks into a folder outside the repository.

| File | What it does |
|---|---|
| `manifest.json` | The tracks: id, class (calm, calm-then-drums, rap, EDM, pop, piano), title, artist, source page, download URL, licence and why each was picked. |
| `fetch.py` | Downloads the tracks and decodes each one to `<id>.wav`: 48 kHz mono 16-bit, the first 240 s. It refuses a folder inside the repository. Needs `ffmpeg`. |
| `oracle.py` | The reference annotation for each track, `<id>.oracle.json`, made with librosa and sharing no code with Rimlight: kick onsets timed in the time domain, the tempo and beats, BS.1770 loudness, quiet/mid/loud sections, and the other onsets (voice, strums, snares). |
| `run_harness.sh` | Replays every track through a build's per-frame work with `wav-analyze --engine` and writes one `<id>.engine.csv` per track. |
| `metrics.py` | Scores one or more runs against the annotations, per track and per class. It measures hit rate, latency, pulse size, bounce depth, false pulses, smoothness, and brightness against loudness. Each measure is defined at the top of the file. |

## Use

```sh
python3 -m venv ~/rimlight-venv && ~/rimlight-venv/bin/pip install -r tools/corpus/requirements.txt
~/rimlight-venv/bin/python -I tools/corpus/fetch.py ~/rimlight-corpus     # about 320 MB of WAV, plus the MP3s
~/rimlight-venv/bin/python -I tools/corpus/oracle.py ~/rimlight-corpus    # HPSS needs 2–3 GB of memory
dotnet build tools/wav-analyze -c Release
sh tools/corpus/run_harness.sh ~/rimlight-corpus ~/rimlight-runs/now --movement Pulse
~/rimlight-venv/bin/python -I tools/corpus/metrics.py ~/rimlight-corpus ~/rimlight-runs/now --md now.md
```

To compare builds, build each one into its own folder, point `WAV_ANALYZE` at that folder's `wav-analyze.dll`, write each run to its own folder, and pass all the run folders to `metrics.py`. `run_harness.sh` passes any extra options on to wav-analyze, for example `--tuning params.json` or `--sensitivity 0.5`.

The pulse measures (hits, pulse size, false pulses, bounce depth, "Shown") describe the Pulse movement, where the whole edge swells on each beat, so every run passes `--movement Pulse`, as the baselines in `docs/PROGRESS.md` were made. In Flow, the app's default since H-033, the whole-edge pulse is only 40 % of the beat and the ripples carry the rest round the edge, which the CSV leaves out: no pulse reaches a hit (0.5), so the hit rate would read 0 %. A build from before the Flow movement has only Pulse and rejects `--movement`, so leave the option out when `WAV_ANALYZE` points at one.

## Results

`docs/PROGRESS.md` → "Feedback round 2" has the per-class tables for the builds before round 1 (5b387fc), after round 1 (6ac553b) and after round 2. Round 2 tuned on 7 tracks: calm-memorized, calm-whispering, drums-hurricane, rap-ballin, rap-kojo, edm-breakers and pop-letitin. It checked the other 8, but not blindly, because every run printed both sets.

## Licences

Every track is under a Creative Commons licence, confirmed from the source's own API (archive.org's `licenseurl`, ccMixter's `license_url`). The licences are BY, BY-NC, BY-NC-SA and BY-NC-ND. The tracks are used only for private, offline analysis and are never redistributed. Never commit the audio, a decoded WAV, a spectrogram, or anything else the sound could be rebuilt from. Annotations and engine CSVs stay out of the repository too.
