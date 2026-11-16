"""Second source for batting hand / bowling arm / style: the official IPL
player profile pages (public pages on iplt20.com: "Batting Style", "Bowling
Style"), for players whose Wikipedia infobox is missing or empty.

    python tools/data/ipl_styles.py [--fetch]

Player links come from the ten franchises' public squad pages. Pages are cached
in build/iplt20/. Writes data/source/research/player_styles_ipl.csv in the same
columns as player_styles.csv; tools/data/apply_styles.py uses it where the
Wikipedia row has no style.
"""
from __future__ import annotations

import argparse
import csv
import difflib
import os
import re
import time
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
REF = os.path.join(ROOT, 'data', 'source', 'players_reference.csv')
WIKI = os.path.join(ROOT, 'data', 'source', 'research', 'player_styles.csv')
OUT = os.path.join(ROOT, 'data', 'source', 'research', 'player_styles_ipl.csv')
CACHE = os.path.join(ROOT, 'build', 'iplt20')
UA = {'User-Agent': 'Mozilla/5.0 (APL data builder; research script)'}
SITE = 'https://www.iplt20.com'
TEAMS = {'CSK': 'chennai-super-kings', 'DC': 'delhi-capitals', 'GT': 'gujarat-titans', 'KKR': 'kolkata-knight-riders',
         'LSG': 'lucknow-super-giants', 'MI': 'mumbai-indians', 'PBKS': 'punjab-kings', 'RR': 'rajasthan-royals',
         'RCB': 'royal-challengers-bengaluru', 'SRH': 'sunrisers-hyderabad'}


def get(path: str, fetch: bool) -> str:
    os.makedirs(CACHE, exist_ok=True)
    local = os.path.join(CACHE, re.sub(r'[^A-Za-z0-9_.-]', '_', path.strip('/')) + '.html')
    if os.path.exists(local):
        with open(local, encoding='utf-8') as f:
            return f.read()
    if not fetch:
        return ''
    try:
        with urllib.request.urlopen(urllib.request.Request(SITE + path, headers=UA), timeout=40) as r:
            text = r.read().decode('utf-8', 'replace')
    except Exception:
        text = ''
    time.sleep(0.5)
    with open(local, 'w', encoding='utf-8') as f:
        f.write(text)
    return text


def field(html: str, label: str) -> str:
    m = re.search(r'>' + label + r'</dt><dd[^>]*>([^<]*)</dd>', html)
    return m.group(1).strip() if m else ''


def slug_name(slug: str) -> str:
    return slug.replace('-', ' ').lower()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--fetch', action='store_true')
    a = ap.parse_args()
    sys_path = os.path.dirname(__file__)
    import sys
    sys.path.insert(0, sys_path)
    from wiki_styles import classify  # same wording rules as the Wikipedia pass

    with open(REF, encoding='utf-8-sig', newline='') as f:
        refs = list(csv.DictReader(f))
    with open(WIKI, encoding='utf-8', newline='') as f:
        wiki = {r['ref']: r for r in csv.DictReader(f)}
    links = {}  # team -> {name: path}
    for team, slug in TEAMS.items():
        html = get(f'/teams/{slug}/squad', a.fetch)
        links[team] = {slug_name(s): p for p, s in re.findall(r'href="(/players/[0-9a-f-]+/([a-z0-9-]+))"', html)}

    rows = []
    for r in refs:
        w = wiki.get(r['ref'], {})
        if r['bowl'] == 'none' or (w.get('arm') and w.get('style')):
            continue
        team = r['real_team']
        cands = links.get(team, {})
        name = r['real_name'].lower().replace('.', '')
        hit = difflib.get_close_matches(name, list(cands), 1, 0.8)
        if not hit:
            # Squad pages sometimes carry a middle name ("emanjot singh chahal").
            parts = name.split()
            hit = [k for k in cands if k.split()[0] == parts[0] and k.split()[-1] == parts[-1]][:1]
        if not hit:
            rows.append({'ref': r['ref'], 'ipl_page': '', 'batting': '', 'bowling': '', 'hand': '', 'arm': '', 'style': '', 'source': ''})
            continue
        path = cands[hit[0]]
        html = get(path, a.fetch)
        batting, bowling = field(html, 'Batting Style'), field(html, 'Bowling Style')
        hand, arm, style = classify(batting, bowling)
        rows.append({'ref': r['ref'], 'ipl_page': hit[0], 'batting': batting, 'bowling': bowling,
                     'hand': hand, 'arm': arm, 'style': style, 'source': SITE + path})
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, ['ref', 'ipl_page', 'batting', 'bowling', 'hand', 'arm', 'style', 'source'], lineterminator='\n')
        w.writeheader()
        w.writerows(rows)
    print(f'{len(rows)} players looked up; styled: {sum(1 for x in rows if x["style"])} -> {os.path.relpath(OUT, ROOT)}')


if __name__ == '__main__':
    main()
