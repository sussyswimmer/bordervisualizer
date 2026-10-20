# README art

The README's header, footer and pictures, regenerated from the app's own code. Needs Python 3 with numpy and Pillow, plus ffmpeg.

```sh
python3 tools/readme-art/header.py assets                      # header.svg and footer.svg, from banner.svg
dotnet build tools/wav-analyze -c Release
dotnet tools/wav-analyze/bin/Release/net8.0/wav-analyze.dll generate groove.wav --bpm 120 --seconds 24 --kick-db -3 --bass-db -14 --hat-db -22 --vocal-db -20
dotnet tools/wav-analyze/bin/Release/net8.0/wav-analyze.dll groove.wav --engine --no-plot --fps 50 --jitter 0 --brightness 1
python3 tools/readme-art/render.py groove.engine.csv assets    # preview.gif and look-*.png
```

`render.py` draws the glow with a line-for-line port of `Glow.hlsl`, driven frame by frame by the real beat engine's output for a synthetic track.
