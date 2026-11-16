"""Appearance notes for the in-game avatars (broad traits only, no photos).

Writes data/source/players_look.csv (edit it freely; re-running keeps your edits
unless --reset) and client-src/Assets/Resources/Data/player_looks.json, which
the game's avatar painter reads.

Fields:
  skin        1 fair .. 6 deep
  hair        buzz | short | side | quiff | curly | long | bald | spiky
  hair_color  black | darkbrown | brown | blond | ginger | grey | salt
  beard       none | stubble | short | full | moustache | goatee
  age         young | prime | senior
  extra       none | glasses | patka | headband

  python tools/gen_looks.py [--reset]
"""
import csv
import json
import os
import random
import sys

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REF = os.path.join(REPO, 'data', 'source', 'players_reference.csv')
OUT_CSV = os.path.join(REPO, 'data', 'source', 'players_look.csv')
OUT_JSON = os.path.join(REPO, 'client-src', 'Assets', 'Resources', 'Data', 'player_looks.json')
FIELDS = ['skin', 'hair', 'hair_color', 'beard', 'age', 'extra']

# Broad traits for well-known players: skin, hair, colour, beard, age.
KNOWN = {
    'r216': (3, 'quiff', 'black', 'full', 'prime'), 'r001': (3, 'short', 'salt', 'short', 'senior'),
    'r129': (3, 'short', 'black', 'short', 'senior'), 'r113': (3, 'quiff', 'black', 'stubble', 'prime'),
    'r136': (4, 'short', 'black', 'short', 'prime'), 'r131': (4, 'quiff', 'darkbrown', 'full', 'prime'),
    'r063': (3, 'quiff', 'black', 'stubble', 'young'), 'r138': (4, 'short', 'black', 'short', 'prime'),
    'r169': (3, 'short', 'black', 'short', 'prime'), 'r035': (3, 'side', 'black', 'full', 'prime'),
    'r071': (3, 'short', 'black', 'short', 'prime'), 'r190': (4, 'short', 'black', 'stubble', 'young'),
    'r004': (4, 'short', 'black', 'full', 'prime'), 'r181': (3, 'short', 'black', 'full', 'senior'),
    'r188': (3, 'short', 'black', 'none', 'young'), 'r228': (3, 'quiff', 'black', 'stubble', 'young'),
    'r238': (1, 'short', 'brown', 'short', 'prime'), 'r239': (1, 'short', 'brown', 'stubble', 'prime'),
    'r007': (3, 'short', 'black', 'stubble', 'prime'), 'r033': (1, 'short', 'blond', 'stubble', 'senior'),
    'r054': (1, 'short', 'brown', 'stubble', 'senior'), 'r085': (4, 'short', 'black', 'short', 'prime'),
    'r110': (4, 'short', 'black', 'short', 'prime'), 'r152': (3, 'short', 'black', 'full', 'young'),
    'r229': (1, 'bald', 'brown', 'full', 'prime'), 'r087': (1, 'short', 'brown', 'none', 'young'),
    'r031': (3, 'short', 'black', 'short', 'prime'), 'r084': (5, 'buzz', 'black', 'stubble', 'senior'),
    'r141': (3, 'short', 'black', 'stubble', 'young'), 'r167': (3, 'short', 'black', 'short', 'senior'),
    'r030': (3, 'short', 'black', 'short', 'prime'), 'r061': (4, 'short', 'black', 'full', 'prime'),
    'r086': (4, 'side', 'black', 'short', 'prime'), 'r089': (4, 'curly', 'black', 'none', 'young'),
    'r120': (4, 'short', 'black', 'full', 'senior'), 'r232': (3, 'quiff', 'black', 'stubble', 'prime'),
    'r009': (3, 'short', 'black', 'full', 'prime'), 'r044': (1, 'short', 'brown', 'full', 'senior'),
    'r058': (6, 'buzz', 'black', 'short', 'prime'), 'r060': (4, 'curly', 'black', 'stubble', 'young'),
    'r148': (1, 'short', 'blond', 'stubble', 'senior'), 'r177': (5, 'buzz', 'black', 'short', 'prime'),
    'r205': (3, 'short', 'black', 'short', 'senior'), 'r207': (1, 'short', 'blond', 'short', 'prime'),
    'r112': (1, 'short', 'brown', 'stubble', 'prime'), 'r144': (1, 'short', 'brown', 'short', 'prime'),
    'r162': (2, 'short', 'black', 'full', 'prime'), 'r184': (3, 'short', 'black', 'stubble', 'young'),
    'r208': (1, 'short', 'brown', 'stubble', 'senior'), 'r210': (3, 'short', 'black', 'full', 'prime'),
    'r213': (2, 'short', 'darkbrown', 'full', 'prime'), 'r245': (1, 'buzz', 'brown', 'full', 'prime'),
    'r103': (1, 'short', 'brown', 'short', 'prime'), 'r176': (3, 'short', 'black', 'stubble', 'young'),
    'r215': (3, 'short', 'black', 'short', 'prime'), 'r049': (3, 'short', 'black', 'stubble', 'prime'),
    'r072': (6, 'buzz', 'black', 'short', 'prime'), 'r053': (2, 'short', 'black', 'stubble', 'prime'),
    'r096': (3, 'curly', 'black', 'none', 'young'), 'r076': (3, 'short', 'black', 'short', 'senior'),
    'r065': (3, 'short', 'black', 'full', 'prime'), 'r088': (6, 'buzz', 'black', 'short', 'prime'),
    'r118': (4, 'short', 'black', 'short', 'prime'), 'r098': (4, 'short', 'black', 'short', 'prime'),
    'r014': (3, 'short', 'black', 'none', 'young'), 'r013': (1, 'short', 'brown', 'none', 'young'),
    'r042': (1, 'short', 'brown', 'none', 'young'), 'r050': (6, 'buzz', 'black', 'short', 'prime'),
    'r020': (1, 'short', 'brown', 'short', 'prime'), 'r045': (1, 'short', 'brown', 'stubble', 'prime'),
    'r046': (1, 'short', 'brown', 'stubble', 'prime'), 'r091': (1, 'quiff', 'blond', 'none', 'young'),
    'r015': (4, 'short', 'black', 'short', 'prime'),
}

SOUTH_ASIA = {'India', 'Pakistan', 'Afghanistan', 'Nepal', 'UAE'}
ISLAND_ASIA = {'Sri Lanka', 'Bangladesh'}
CARIBBEAN = {'West Indies'}
WHITE_MAJORITY = {'Australia', 'England', 'New Zealand', 'Ireland', 'Scotland', 'Netherlands'}


def pick(rng, weighted):
    total = sum(w for _, w in weighted)
    x = rng.random() * total
    for value, w in weighted:
        x -= w
        if x <= 0:
            return value
    return weighted[-1][0]


def default_look(r):
    rng = random.Random(r['ref'])
    c = r['country']
    if c in SOUTH_ASIA:
        skin = pick(rng, [(3, 5), (4, 4), (2, 1)])
        colors = [('black', 1)]
        beards = [('none', 2), ('stubble', 3), ('short', 4), ('full', 2)]
    elif c in ISLAND_ASIA:
        skin = pick(rng, [(4, 5), (5, 2), (3, 1)])
        colors = [('black', 1)]
        beards = [('none', 3), ('stubble', 3), ('short', 3), ('moustache', 1)]
    elif c in CARIBBEAN:
        skin = pick(rng, [(6, 4), (5, 4), (4, 1)])
        colors = [('black', 1)]
        beards = [('none', 2), ('stubble', 3), ('short', 3), ('goatee', 1)]
    elif c == 'South Africa' or c == 'Zimbabwe':
        skin = pick(rng, [(1, 5), (6, 3), (4, 1)])
        colors = [('brown', 3), ('darkbrown', 2), ('blond', 1)] if skin <= 2 else [('black', 1)]
        beards = [('none', 2), ('stubble', 3), ('short', 3), ('full', 1)]
    else:
        skin = pick(rng, [(1, 6), (2, 3)])
        colors = [('brown', 4), ('darkbrown', 3), ('blond', 2), ('ginger', 1)]
        beards = [('none', 3), ('stubble', 4), ('short', 2), ('full', 1)]
    hair = 'buzz' if skin >= 5 and rng.random() < 0.6 else pick(rng, [('short', 5), ('side', 2), ('quiff', 2), ('curly', 1), ('spiky', 1)])
    young = r['capped'] == 'N' and rng.random() < 0.7
    age = 'young' if young else pick(rng, [('prime', 4), ('senior', 1)])
    beard = 'none' if age == 'young' and rng.random() < 0.5 else pick(rng, beards)
    color = pick(rng, colors)
    if age == 'senior' and rng.random() < 0.3:
        color = 'salt'
    extra = 'glasses' if rng.random() < 0.04 else 'headband' if rng.random() < 0.05 else 'none'
    return {'skin': skin, 'hair': hair, 'hair_color': color, 'beard': beard, 'age': age, 'extra': extra}


def main():
    reset = '--reset' in sys.argv
    with open(REF, encoding='utf-8') as f:
        refs = list(csv.DictReader(f))
    existing = {}
    if os.path.exists(OUT_CSV) and not reset:
        with open(OUT_CSV, encoding='utf-8') as f:
            for row in csv.DictReader(f):
                if 'skin' in row and row.get('skin'):
                    existing[row['id']] = row
    rows = []
    for r in refs:
        if r['ref'] in existing:
            rows.append(existing[r['ref']])
            continue
        look = default_look(r)
        if r['ref'] in KNOWN:
            k = KNOWN[r['ref']]
            look.update({'skin': k[0], 'hair': k[1], 'hair_color': k[2], 'beard': k[3], 'age': k[4], 'extra': 'none'})
        rows.append({'id': r['ref'], 'game_name': r['game_name'], **look})
    with open(OUT_CSV, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['id', 'game_name'] + FIELDS)
        w.writeheader()
        w.writerows(rows)
    looks = {row['id']: {k: (int(row[k]) if k == 'skin' else row[k]) for k in FIELDS} for row in rows}
    os.makedirs(os.path.dirname(OUT_JSON), exist_ok=True)
    with open(OUT_JSON, 'w', encoding='utf-8') as f:
        json.dump(looks, f, separators=(',', ':'))
    print(f'{len(rows)} looks -> {OUT_CSV} and {OUT_JSON}')


if __name__ == '__main__':
    main()
