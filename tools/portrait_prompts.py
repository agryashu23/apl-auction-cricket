"""Portrait prompts for an external image tool, and importing the results.

Prompts come ONLY from broad traits (data/source/players_look.csv + country):
never a player's name or photo, so each portrait is a believable cricketer of
the right type rather than a likeness. A seeded face shape and build keep the
329 faces from converging on one look.

  python tools/portrait_prompts.py prompts            # -> data/art/portrait_prompts.csv (+ .txt)
  python tools/portrait_prompts.py import <folder>    # <id>.png files -> Resources/Portraits (256 px)

Generate each prompt in a FRESH chat/session (earlier context drags faces toward
whoever was mentioned before), save the image as <id>.png (e.g. r129.png).
"""
import csv
import os
import random
import subprocess
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOOKS = os.path.join(REPO, 'data', 'source', 'players_look.csv')
REF = os.path.join(REPO, 'data', 'source', 'players_reference.csv')
OUT_DIR = os.path.join(REPO, 'data', 'art')
PORTRAITS = os.path.join(REPO, 'client-src', 'Assets', 'Resources', 'Portraits')

ORIGIN = {
    'India': 'Indian', 'Pakistan': 'Pakistani', 'Afghanistan': 'Afghan', 'Sri Lanka': 'Sri Lankan',
    'Bangladesh': 'Bangladeshi', 'Nepal': 'Nepali', 'West Indies': 'Caribbean', 'Australia': 'Australian',
    'England': 'English', 'New Zealand': 'New Zealander', 'Ireland': 'Irish', 'Scotland': 'Scottish',
    'Netherlands': 'Dutch', 'South Africa': 'South African', 'Zimbabwe': 'Zimbabwean', 'UAE': 'Emirati',
}
SKIN = {1: 'fair skin', 2: 'light skin', 3: 'light brown skin', 4: 'brown skin', 5: 'dark brown skin', 6: 'deep dark skin'}
AGE = {'young': 'early 20s', 'prime': 'late 20s', 'senior': 'late 30s'}
HAIR = {
    'short': 'short neat {c} hair', 'side': 'side-parted {c} hair', 'quiff': '{c} hair styled up in a quiff',
    'curly': 'short curly {c} hair', 'spiky': 'short spiky {c} hair', 'buzz': 'very short buzz-cut {c} hair',
    'long': 'shoulder-length {c} hair', 'bald': 'a shaved head',
}
COLOR = {'black': 'black', 'darkbrown': 'dark brown', 'brown': 'brown', 'blond': 'blond', 'ginger': 'ginger',
         'grey': 'grey', 'salt': 'salt-and-pepper'}
BEARD = {'none': 'clean-shaven', 'stubble': 'light stubble', 'short': 'a short trimmed beard',
         'full': 'a full beard', 'moustache': 'a moustache', 'goatee': 'a goatee'}
FACES = ['an oval face', 'a round face', 'a long face', 'a square jaw', 'a heart-shaped face', 'high cheekbones']
BUILDS = ['lean build', 'athletic build', 'broad-shouldered build', 'stocky build']
MOODS = ['warm smile', 'confident half-smile', 'calm focused look', 'friendly grin']


def prompt(look, country, pid):
    rng = random.Random('portrait:' + pid)
    origin = ORIGIN.get(country, '')
    hair = HAIR.get(look['hair'], HAIR['short']).format(c=COLOR.get(look['hair_color'], 'dark'))
    extra = {'glasses': ', wearing thin-framed glasses', 'headband': ', wearing a sports headband',
             'patka': ', wearing a navy patka'}.get(look['extra'], '')
    # One person, clearly a man, every trait spelled out; batch/grid generation ignores
    # per-prompt details, so each prompt is meant to be its own image.
    glasses = '' if look['extra'] == 'glasses' else ', no glasses'
    return (f"single portrait of ONE adult male cricketer (a man, masculine features), stylised digital painting, "
            f"head and shoulders, fictional {origin} man in his {AGE[look['age']]}, "
            f"{SKIN[int(look['skin'])]}, {rng.choice(FACES)}, {rng.choice(BUILDS)}, {hair}, {BEARD[look['beard']]}"
            f"{extra}{glasses}, {rng.choice(MOODS)}, plain navy training polo with gold trim, no logos or text, "
            f"blue gradient background, facing the camera").replace('  ', ' ')


# For tools with a "negative prompt" / "avoid" box.
NEGATIVE = 'woman, female, girl, feminine, long eyelashes, earrings, makeup, more than one person, grid, collage, text, logo'


def write_prompts():
    with open(REF, encoding='utf-8') as f:
        country = {r['ref']: r['country'] for r in csv.DictReader(f)}
    with open(LOOKS, encoding='utf-8') as f:
        looks = list(csv.DictReader(f))
    os.makedirs(OUT_DIR, exist_ok=True)
    # No names in these files: whoever generates the images sees only traits; we map by file name.
    rows = [{'file': r['id'] + '.png', 'prompt': prompt(r, country.get(r['id'], ''), r['id'])} for r in looks]
    with open(os.path.join(OUT_DIR, 'portrait_prompts.csv'), 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['file', 'prompt'])
        w.writeheader()
        w.writerows(rows)
    with open(os.path.join(OUT_DIR, 'portrait_prompts.txt'), 'w', encoding='utf-8') as f:
        f.write('One image per prompt (not a grid). If the tool has an "avoid"/negative box, use:\n')
        f.write(NEGATIVE + '\n\n')
        for r in rows:
            f.write(f"{r['file']}\n{r['prompt']}\n\n")
    print(f'{len(rows)} prompts -> {OUT_DIR}')


def import_folder(folder):
    os.makedirs(PORTRAITS, exist_ok=True)
    ids = set()
    with open(LOOKS, encoding='utf-8') as f:
        ids = {r['id'] for r in csv.DictReader(f)}
    done = skipped = 0
    for name in sorted(os.listdir(folder)):
        stem, ext = os.path.splitext(name)
        if ext.lower() not in ('.png', '.jpg', '.jpeg', '.webp') or stem not in ids:
            skipped += 1
            continue
        out = os.path.join(PORTRAITS, stem + '.png')
        # Centre-crop to a square, 256 px: small in the APK, sharp in the hexagon.
        subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', os.path.join(folder, name), '-vf',
                        "crop='min(iw,ih)':'min(iw,ih)',scale=256:256:flags=lanczos", out], check=True)
        done += 1
    print(f'imported {done} portraits -> {PORTRAITS} (skipped {skipped} files not named <player id>)')


if __name__ == '__main__':
    if len(sys.argv) >= 2 and sys.argv[1] == 'import' and len(sys.argv) >= 3:
        import_folder(sys.argv[2])
    else:
        write_prompts()
