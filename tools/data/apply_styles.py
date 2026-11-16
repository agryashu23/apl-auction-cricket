"""Applies the researched batting hand, bowling arm and bowling style
(data/source/research/player_styles.csv, from tools/data/wiki_styles.py) and
the reviewed capped flags to data/source/players_reference.csv (Wikipedia
first, then the iplt20.com profile from tools/data/ipl_styles.py, then the
hand-checked MANUAL rows). Idempotent;
every change is appended to data/source/research/ipl2026_squad_changes.csv
with its source.

    python tools/data/apply_styles.py

Columns added to the sheet: arm (L/R) and style (fast | fast-medium | medium |
finger | wrist). Blank = unknown: the engine falls back to its old stable hash
for that bowler (Bowling.LeftArm / KindFor).
"""
import csv
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
REF = os.path.join(ROOT, 'data', 'source', 'players_reference.csv')
STYLES = os.path.join(ROOT, 'data', 'source', 'research', 'player_styles.csv')
LOG = os.path.join(ROOT, 'data', 'source', 'research', 'ipl2026_squad_changes.csv')
IPL = 'https://www.iplt20.com/news/'

# Players with no usable Wikipedia infobox, from the public signing notes.
MANUAL = {
    'r331': ('R', 'medium', IPL + '4295/mumbai-indians-sign-krish-bhagat-as-a-replacement-for-atharva-ankolekar'),
    'r334': ('L', 'fast', 'https://en.wikipedia.org/wiki/Saurabh_Dubey_(Maharashtra_cricketer) (NDTV/Outlook refs: left-arm fast)'),
    'r342': ('L', 'finger', IPL + '4368/rajasthan-royals-sign-emanjot-chahal-as-a-replacement-for-injured-ravi-singh'),
    # iplt20.com player profiles (Bowling Style field / profile text)
    'r198': ('R', 'medium', 'https://www.iplt20.com/players/718b6df9-e6c1-4c48-94e7-fbc4f32584ac/brijesh-sharma (Right Arm Medium)'),
    'r106': ('R', 'medium', 'https://www.iplt20.com/players/arshin-kulkarni/20691 (profile: right-arm medium pace)'),
    'r041': ('R', 'finger', 'https://www.iplt20.com/players/tripurana-vijay/22252 (profile: right-arm off-break bowler)'),
    'r196': ('R', 'wrist', 'https://documents.iplt20.com/bcci/documents/1765197869375_TATA-IPL-2026-Auction-List-8.12.25.pdf '
                           '(BCCI auction list row: RHB, RIGHT ARM Leg Spin; quoted by search index, PDF now 403)'),
    'r221': ('L', 'fast-medium', 'https://www.cricketcountry.com/news/rcb-new-rs-52000000-crore-left-arm-pace-sensation-still-cant-believe-his-ipl-2026-call-up-his-name-is-mangesh-yadav-1316589/ (left-arm pacer)'),
    'r073': ('R', 'fast', 'https://www.iplt20.com/video/64620/1542-kmph-ashok-sharma-bowls-the-fastest-ball-of-tata-ipl-2026 '
                          '(154.2 km/h, fastest ball of IPL 2026; IPL squad page: right-arm)'),
    'r034': ('R', 'fast-medium', 'https://www.delhicapitals.in/players/124649-madhav-tiwari-profile (Bowling Style: Right-arm medium fast)'),
}
IPL_STYLES = os.path.join(ROOT, 'data', 'source', 'research', 'player_styles_ipl.csv')
CAPPED = {
    'r179': ('Y', 'https://en.wikipedia.org/wiki/Lhuan-dre_Pretorius (Test debut 28 Jun 2025, T20I 14 Jul 2025)'),
    'r273': ('Y', 'https://en.wikipedia.org/wiki/Vijay_Shankar (India ODI/T20I cap)'),
}


def main():
    with open(REF, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    header = list(rows[0].keys())
    for col in ('arm', 'style'):
        if col not in header:
            header.append(col)
            for r in rows:
                r[col] = ''
    with open(STYLES, encoding='utf-8', newline='') as f:
        styles = {r['ref']: r for r in csv.DictReader(f)}
    ipl = {}
    if os.path.exists(IPL_STYLES):
        with open(IPL_STYLES, encoding='utf-8', newline='') as f:
            ipl = {r['ref']: r for r in csv.DictReader(f) if r['style']}
    log = []

    def change(r, field, new, src):
        old = r.get(field, '')
        if str(old) != str(new):
            log.append({'ref': r['ref'], 'player': r['real_name'], 'field': field, 'old': old, 'new': new, 'source': src})
            r[field] = str(new)

    for r in rows:
        s = styles.get(r['ref'])
        if s and s['hand'] and s['hand'] != r['hand']:
            change(r, 'hand', s['hand'], s['source'] + f' (batting: {s["batting"]})')
        elif not (s and s['hand']) and r['ref'] in ipl and ipl[r['ref']]['hand'] and ipl[r['ref']]['hand'] != r['hand']:
            change(r, 'hand', ipl[r['ref']]['hand'], ipl[r['ref']]['source'] + f' (Batting Style: {ipl[r["ref"]]["batting"]})')
        if r['bowl'] == 'none':
            continue
        if r['ref'] in MANUAL:
            arm, style, src = MANUAL[r['ref']]
        elif s and s['arm'] and s['style']:
            arm, style, src = s['arm'], s['style'], s['source'] + f' (bowling: {s["bowling"]})'
        elif r['ref'] in ipl:
            i = ipl[r['ref']]
            arm, style, src = i['arm'], i['style'], i['source'] + f' (Bowling Style: {i["bowling"]})'
        else:
            continue
        spin = style in ('finger', 'wrist')
        if (r['bowl'] == 'spin') != spin:
            change(r, 'bowl', 'spin' if spin else 'pace', src)
        change(r, 'arm', arm, src)
        change(r, 'style', style, src)
    for ref, (capped, src) in CAPPED.items():
        r = next(x for x in rows if x['ref'] == ref)
        change(r, 'capped', capped, src)

    with open(REF, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, header, lineterminator='\n')
        w.writeheader()
        w.writerows(rows)
    with open(LOG, 'a', encoding='utf-8', newline='') as f:
        csv.DictWriter(f, ['ref', 'player', 'field', 'old', 'new', 'source'], lineterminator='\n').writerows(log)
    by = {}
    for l in log:
        by[l['field']] = by.get(l['field'], 0) + 1
    print(f'{len(log)} changes {by}; bowlers still without arm/style: '
          f'{sum(1 for r in rows if r["bowl"] != "none" and not r["style"])}')


if __name__ == '__main__':
    main()
