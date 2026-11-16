"""Round-8 commentary top-up (English only) with Amazon Polly
(AWS CLI profile `default`).

    aws login --profile default          # if the session has expired
    python tools/gen_commentary_r8.py    # add --dry-run to list what it would make

Reads tools/commentary_more_en_3.json, appends each event's new lines after the
ones audio/voice/commentary.json already has (the same keys gen_commentary.py
would give them: an event's own lines, then commentary_more_en_1, _2, _3), writes
audio/voice/comm_en/<event>_<n>.mp3 and updates the manifest. Arjun leads, Kiara
is the second voice, as before. Nothing else in the manifest is touched.
"""
import json
import os
import subprocess
import sys
import tempfile
import time
from concurrent.futures import ThreadPoolExecutor

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
PROFILE = 'default'
VOICES = {'lead': 'Arjun', 'colour': 'Kiara'}


def polly(text, voice, out):
    with tempfile.NamedTemporaryFile('w', suffix='.txt', delete=False, encoding='utf-8') as f:
        f.write(text)
        path = f.name
    try:
        for attempt in range(5):
            # Polly throttles bursts (exit 254): back off and try again.
            if subprocess.run(['aws', 'polly', 'synthesize-speech', '--profile', PROFILE, '--region', 'us-east-1',
                        '--engine', 'generative', '--voice-id', voice, '--language-code', 'en-IN',
                        '--output-format', 'mp3', '--sample-rate', '24000',
                        '--text', 'file://' + path.replace('\\', '/'), out],
                       stdout=subprocess.DEVNULL, env=dict(os.environ, AWS_CLI_FILE_ENCODING='UTF-8')).returncode == 0:
                return
            time.sleep(2 + 3 * attempt)
        raise RuntimeError('polly failed: ' + out)
    finally:
        os.remove(path)


def main(dry):
    manifest_path = os.path.join(REPO, 'audio', 'voice', 'commentary.json')
    manifest = json.load(open(manifest_path, encoding='utf-8'))
    events, texts, named = manifest['events']['en'], manifest['texts']['en'], manifest['named']['en']
    # The lines file: commentary_more_en_3.json (round 8), or --file <name> under tools/ (round 9: _4).
    name = sys.argv[sys.argv.index('--file') + 1] if '--file' in sys.argv else 'commentary_more_en_3.json'
    more = json.load(open(os.path.join(REPO, 'tools', name), encoding='utf-8'))
    out_dir = os.path.join(REPO, 'audio', 'voice', 'comm_en')
    jobs = []

    def clip(key, text, role):
        texts[key] = text
        out = os.path.join(out_dir, key + '.mp3')
        if not os.path.exists(out):
            jobs.append((text, VOICES[role], out))

    # A re-run doesn't add an event's lines twice. (A later full gen_commentary.py run picks
    # commentary_more_en_3.json up by itself and rebuilds the manifest without this list.)
    added = manifest.setdefault('r8_added', [])
    for event, variants in more.items():
        if event == 'about' or event in added:
            continue
        added.append(event)
        keys = events.setdefault(event, [])
        for i, (role, text) in enumerate(variants, len(keys) + 1):
            key = f'{event}_{i}'
            keys.append(key)
            for slot in ('bat', 'bowl'):
                tag = '{' + slot + '}'
                if tag in text:
                    before, after = text.split(tag, 1)
                    at = 'start' if not before.strip() else 'mid' if after.strip(' ,.!?') else 'end'
                    named[key] = {'slot': slot, 'at': at}
                    if at == 'start':
                        text = after.strip(' ,')
                    else:
                        text = before.rstrip(' ,')
                        if at == 'mid':
                            clip(key + '_b', after.strip(' ,').lstrip('!.?, '), role)
            clip(key, text, role)
    for text, voice, out in jobs:
        print(f'{os.path.basename(out):28s} {voice:6s} {text}')
    if dry:
        print(f'dry run: {len(jobs)} clips would be made')
        return
    os.makedirs(out_dir, exist_ok=True)
    with ThreadPoolExecutor(3) as pool:
        list(pool.map(lambda j: polly(*j), jobs))
    json.dump(manifest, open(manifest_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f'commentary r8: {len(jobs)} clips synthesized; manifest updated')


if __name__ == '__main__':
    main('--dry-run' in sys.argv)
