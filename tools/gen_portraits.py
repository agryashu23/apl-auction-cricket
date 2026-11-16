"""Illustrated player portraits via Amazon Bedrock (Stable Diffusion 3.5 Large, us-west-2).

Prompts are built ONLY from the appearance fields in data/source/players_look.csv
(tools/gen_looks.py: skin tone, hair, facial hair, age band, extras) — never from a player's name or
a photo. Output: client-src/Assets/Resources/Portraits/<player id>.png (256 px),
which the game picks up automatically. Re-runnable: existing portraits are skipped.

  python tools/gen_portraits.py --sample 4      # a few, to check the style
  python tools/gen_portraits.py                 # everything missing
"""
import argparse
import base64
import csv
import io
import json
import os
import subprocess
import sys
import tempfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, 'client-src', 'Assets', 'Resources', 'Portraits')
LOOKS = os.path.join(REPO, 'data', 'source', 'players_look.csv')
MODEL = 'stability.sd3-5-large-v1:0'

STYLE = ('Stylised digital painting, head and shoulders portrait of a fictional {look}, professional cricketer, '
         'wearing a plain solid dark navy crew-neck training t-shirt that is completely blank, '
         'dark charcoal studio background with a soft orange rim light, sports trading card illustration, '
         'centered, facing the camera, sharp focus')
NEGATIVE = ('text, letters, numbers, logo, emblem, badge, crest, brand mark, sponsor, collar badge, flag, '
            'watermark, signature, helmet, cap, hat, sunglasses, frame, border, blurry, deformed face, extra people')


SKIN = ['', 'fair-skinned', 'light-skinned', 'medium-brown-skinned', 'brown-skinned', 'dark-brown-skinned', 'deep-dark-skinned']
AGE = {'young': 'in his early twenties', 'prime': 'in his late twenties', 'senior': 'in his late thirties'}


def describe(r):
    hair = 'shaved head' if r['hair'] == 'bald' else f"{r['hair']} {r['hair_color'].replace('salt', 'salt-and-pepper').replace('darkbrown', 'dark brown')} hair"
    beard = {'none': 'clean-shaven', 'stubble': 'light stubble', 'short': 'short beard', 'full': 'full beard',
             'moustache': 'moustache', 'goatee': 'goatee'}[r['beard']]
    extra = '' if r['extra'] == 'none' else f", wearing {'a ' + r['extra'] if r['extra'] != 'glasses' else 'glasses'}"
    return f"{SKIN[int(r['skin'])]} man {AGE[r['age']]}, {hair}, {beard}{extra}"


def generate(prompt, seed, path):
    body = {'prompt': prompt, 'negative_prompt': NEGATIVE, 'mode': 'text-to-image',
            'aspect_ratio': '1:1', 'output_format': 'png', 'seed': seed}
    with tempfile.TemporaryDirectory() as tmp:
        req = os.path.join(tmp, 'req.json')
        res = os.path.join(tmp, 'res.json')
        with open(req, 'w', encoding='utf-8') as f:
            json.dump(body, f)
        subprocess.run(['aws', 'bedrock-runtime', 'invoke-model', '--model-id', MODEL, '--region', 'us-west-2',
                        '--profile', 'default', '--body', 'fileb://' + req, '--cli-read-timeout', '180', res],
                       check=True, capture_output=True)
        with open(res, encoding='utf-8') as f:
            data = json.load(f)
    png = base64.b64decode(data['images'][0])
    # Downscale to 256 px with ffmpeg (already used by the repo's tools).
    with tempfile.NamedTemporaryFile(suffix='.png', delete=False) as raw:
        raw.write(png)
    subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', raw.name, '-vf', 'scale=256:256:flags=lanczos', path], check=True)
    os.remove(raw.name)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--sample', type=int, default=0, help='only the first N rows')
    ap.add_argument('--only', default='', help='comma-separated player ids')
    args = ap.parse_args()
    os.makedirs(OUT, exist_ok=True)
    with open(LOOKS, encoding='utf-8') as f:
        rows = [r for r in csv.DictReader(f) if r.get('skin')]
    if args.only:
        wanted = set(args.only.split(','))
        rows = [r for r in rows if r['id'] in wanted]
    if args.sample:
        rows = rows[:args.sample]
    made = 0
    for r in rows:
        path = os.path.join(OUT, r['id'] + '.png')
        if os.path.exists(path):
            continue
        seed = int(''.join(ch for ch in r['id'] if ch.isdigit()) or '1')
        look = describe(r)
        generate(STYLE.format(look=look), seed, path)
        made += 1
        print(f"{r['id']}: {look}", flush=True)
    print(f'done: {made} new portraits -> {OUT}')


if __name__ == '__main__':
    sys.exit(main())
