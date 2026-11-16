"""Match commentary and crowd chants with Amazon Polly (us-east-1, AWS CLI profile `default`).

  python tools/gen_commentary.py            # commentary (what's new or changed)
  python tools/gen_commentary.py --chants   # also rebuild the crowd chants (needs ffmpeg)

Commentary: tools/commentary_lines.json -> audio/voice/comm_en/<event>_<n>.mp3
plus audio/voice/commentary.json (event -> clip keys) for the Unity client.
English only (owner): Arjun leads, Kiara is the second voice.

Chants: "A! P! L!" and each home city's name, said by every Indian Polly voice,
pitch-shifted into a few dozen voices, staggered, given stadium echo and mixed
over the crowd bed -> client-src/Assets/Resources/Sfx/chant_*.ogg.

Text goes to the CLI through file:// so it survives the Windows command line.
"""
import json
import os
import random
import subprocess
import sys
import tempfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
PROFILE = 'default'  # AWS CLI profile used for Amazon Polly
VOICES = {'en': {'lead': 'Arjun', 'colour': 'Kiara'}}
LANG = {'en': 'en-IN'}
# English only (owner): no other language is generated or shipped.
LANGS = ('en',)


def polly(text, voice, lang, out, engine='generative'):
    with tempfile.NamedTemporaryFile('w', suffix='.txt', delete=False, encoding='utf-8') as f:
        f.write(text)
        path = f.name
    try:
        subprocess.run(['aws', 'polly', 'synthesize-speech', '--profile', PROFILE, '--region', 'us-east-1',
                        '--engine', engine, '--voice-id', voice, '--language-code', lang,
                        '--output-format', 'mp3', '--sample-rate', '24000' if engine != 'standard' else '22050',
                        '--text', 'file://' + path.replace('\\', '/'), out],
                       check=True, stdout=subprocess.DEVNULL, env=dict(os.environ, AWS_CLI_FILE_ENCODING='UTF-8'))
    finally:
        os.remove(path)


def commentary():
    lines = json.load(open(os.path.join(REPO, 'tools', 'commentary_lines.json'), encoding='utf-8'))
    manifest_path = os.path.join(REPO, 'audio', 'voice', 'commentary.json')
    old = {}
    if os.path.exists(manifest_path):
        for lang, events in json.load(open(manifest_path, encoding='utf-8'))['texts'].items():
            for key, text in events.items():
                old[(lang, key)] = text
    # The extra lines (tools/commentary_more_<lang>_*.json) go on after each event's own.
    import glob
    for lang in LANGS:
        for path in sorted(glob.glob(os.path.join(REPO, 'tools', f'commentary_more_{lang}_*.json'))):
            for event, variants in json.load(open(path, encoding='utf-8')).items():
                if event != 'about':
                    lines[lang].setdefault(event, []).extend(variants)
    players = json.load(open(os.path.join(REPO, 'data', 'apl_dataset.json'), encoding='utf-8'))['players']
    manifest = {'events': {}, 'texts': {}, 'named': {}}
    jobs = []
    for lang in LANGS:
        out_dir = os.path.join(REPO, 'audio', 'voice', 'comm_' + lang)
        os.makedirs(out_dir, exist_ok=True)
        manifest['events'][lang] = {}
        manifest['texts'][lang] = {}
        manifest['named'][lang] = {}

        def clip(key, text, role='lead'):
            manifest['texts'][lang][key] = text
            out = os.path.join(out_dir, key + '.mp3')
            if not (os.path.exists(out) and old.get((lang, key)) == text):
                jobs.append((text, VOICES[lang][role], LANG[lang], out))

        for event, variants in lines[lang].items():
            keys = []
            for i, (role, text) in enumerate(variants, 1):
                key = f'{event}_{i}'
                keys.append(key)
                # A name slot at either end: the clip is the rest; the client says the name clip with it.
                for slot in ('bat', 'bowl'):
                    tag = '{' + slot + '}'
                    if tag in text:
                        before, after = text.split(tag, 1)
                        after_words = after.strip(' ,.!?')
                        at = 'start' if not before.strip() else 'mid' if after_words else 'end'
                        manifest['named'][lang][key] = {'slot': slot, 'at': at}
                        if at == 'start':
                            text = after.strip(' ,')
                        else:
                            # The name closes the first part; in the middle the rest is a clip of its own (key_b).
                            text = before.rstrip(' ,')
                            if at == 'mid':
                                clip(key + '_b', after.strip(' ,').lstrip('!.?, '), role)
                clip(key, text, role)
            manifest['events'][lang][event] = keys
        # Numbers for "they need 12 off 6", and every player's name, said by the lead voice.
        for n in range(1, 61):
            clip(f'num_{n}', str(n))
        for p in players:
            clip(f"name_{p['id']}", p['name'])
    from concurrent.futures import ThreadPoolExecutor
    with ThreadPoolExecutor(8) as pool:
        list(pool.map(lambda j: polly(*j), jobs))
    json.dump(manifest, open(manifest_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f'commentary: {len(jobs)} clips synthesized')


def chants():
    data = json.load(open(os.path.join(REPO, 'data', 'apl_dataset.json'), encoding='utf-8'))
    sfx = os.path.join(REPO, 'client-src', 'Assets', 'Resources', 'Sfx')
    bed = os.path.join(sfx, 'crowd.ogg')
    work = os.path.join(REPO, 'client-assets', 'sfx', 'chant_parts')
    os.makedirs(work, exist_ok=True)
    singers = [('Arjun', 'en-IN', 'generative'), ('Kajal', 'en-IN', 'neural'), ('Kiara', 'en-IN', 'generative'),
               ('Aditi', 'en-IN', 'standard'), ('Raveena', 'en-IN', 'standard')]
    calls = {'apl': 'A! P! L!'}
    for f in data['franchises']:
        calls[f['id'].lower()] = f"{f['city']}! {f['city']}!"
    rng = random.Random(7)
    for name, text in calls.items():
        parts = []
        for voice, lang, engine in singers:
            out = os.path.join(work, f'{name}_{voice}.mp3')
            if not os.path.exists(out):
                polly(text, voice, lang, out, engine)
            parts.append(out)
        # Each singer becomes 6 people: pitch shifted (rate trick), nudged in time.
        inputs, filters, labels = [], [], []
        n = 0
        for p in parts:
            for _ in range(6):
                inputs += ['-i', p]
                rate = rng.uniform(0.86, 1.12)
                delay = int(rng.uniform(0, 110))
                vol = rng.uniform(0.5, 1.0)
                filters.append(f'[{n}:a]aresample=24000,asetrate={int(24000 * rate)},aresample=24000,'
                               f'adelay={delay}|{delay},volume={vol:.2f}[v{n}]')
                labels.append(f'[v{n}]')
                n += 1
        inputs += ['-i', bed]
        filters.append(f'{"".join(labels)}amix=inputs={n}:normalize=0,volume={1.6 / n ** 0.5:.3f},'
                       'highpass=f=180,lowpass=f=5200,aecho=0.8:0.6:70|140|260:0.35|0.25|0.15[voices]')
        filters.append(f'[{n}:a]aresample=24000,volume=0.35[bed]')
        filters.append('[voices][bed]amix=inputs=2:duration=first:normalize=0,'
                       'apad=pad_dur=0.6,loudnorm=I=-16[out]')
        out = os.path.join(sfx, f'chant_{name}.ogg')
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error'] + inputs +
                       ['-filter_complex', ';'.join(filters), '-map', '[out]', '-ac', '1', '-c:a', 'libvorbis', '-q:a', '5', out],
                       check=True)
        print('chant', name)


if __name__ == '__main__':
    commentary()
    if '--chants' in sys.argv:
        chants()
