"""Downloads the corpus tracks listed in manifest.json and decodes them for wav-analyze and oracle.py.

Usage: python -I fetch.py <corpus dir>

Writes <corpus dir>/<id>.wav (48 kHz mono 16-bit PCM, the channels averaged as the app's capture does, soxr
resampling, the first 240 s) and a copy of manifest.json, which oracle.py and metrics.py read from there.
Downloads go to <corpus dir>/download/<id>/ and are kept, so a second run only decodes. Needs ffmpeg and ffprobe
on the PATH.

The corpus dir must be outside the repository: the tracks are Creative Commons licensed for this offline analysis,
and neither the audio nor anything that could reconstruct it is ever committed.
"""
import json, os, subprocess, sys, urllib.parse, urllib.request

MAX_SECONDS = 240
HERE = os.path.dirname(os.path.abspath(__file__))


def inside_repository(path):
    repo = subprocess.run(['git', '-C', HERE, 'rev-parse', '--show-toplevel'], capture_output=True, text=True)
    if repo.returncode != 0:
        return False
    root = os.path.realpath(repo.stdout.strip())
    return os.path.commonpath([root, os.path.realpath(path)]) == root


def download(track, folder):
    url = track['download_url']
    name = ''.join(c if c.isalnum() or c in '._-' else '_' for c in urllib.parse.unquote(url.rsplit('/', 1)[1]))
    path = os.path.join(folder, name)
    if os.path.exists(path) and os.path.getsize(path) > 100_000:
        return path
    os.makedirs(folder, exist_ok=True)
    # ccMixter serves its files only with the upload's page as the Referer.
    request = urllib.request.Request(url, headers={'User-Agent': 'rimlight-corpus (offline test corpus)',
                                                   'Referer': track['source_url']})
    error = None
    for _ in range(3):
        try:
            with urllib.request.urlopen(request, timeout=180) as response, open(path + '.part', 'wb') as out:
                while chunk := response.read(1 << 16):
                    out.write(chunk)
            os.replace(path + '.part', path)
            return path
        except OSError as e:
            error = e
    raise SystemExit(f"{track['id']}: download failed: {error}")


def channels(path):
    probe = subprocess.run(['ffprobe', '-v', 'error', '-select_streams', 'a:0', '-show_entries', 'stream=channels',
                            '-of', 'json', path], check=True, capture_output=True, text=True)
    return int(json.loads(probe.stdout)['streams'][0]['channels'])


def decode(source, out):
    mix = 'pan=mono|c0=0.5*c0+0.5*c1,' if channels(source) == 2 else ''
    subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y', '-i', source, '-t', str(MAX_SECONDS),
                    '-af', mix + 'aresample=48000:resampler=soxr', '-ar', '48000', '-ac', '1', '-c:a', 'pcm_s16le',
                    out], check=True)


def main():
    if len(sys.argv) != 2:
        raise SystemExit(__doc__)
    corpus = os.path.abspath(sys.argv[1])
    if inside_repository(corpus):
        raise SystemExit(f'{corpus} is inside the repository; pick a folder outside it.')
    os.makedirs(corpus, exist_ok=True)
    with open(os.path.join(HERE, 'manifest.json'), encoding='utf-8') as f:
        manifest = json.load(f)
    for track in manifest['tracks']:
        source = download(track, os.path.join(corpus, 'download', track['id']))
        decode(source, os.path.join(corpus, track['wav']))
        print(f"{track['id']:18s} {track['licence_url']}", flush=True)
    with open(os.path.join(corpus, 'manifest.json'), 'w', encoding='utf-8') as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
    print('wrote', len(manifest['tracks']), 'tracks to', corpus)


if __name__ == '__main__':
    main()
