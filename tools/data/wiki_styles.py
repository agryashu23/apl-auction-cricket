"""Batting hand, bowling arm and bowling style for every dataset player, from
the Wikipedia cricketer infoboxes (public pages; not Cricinfo).

    python tools/data/wiki_styles.py            # uses the cache in build/wiki/
    python tools/data/wiki_styles.py --fetch    # (re)download missing pages

Article titles come from the links on "List of 2026 Indian Premier League
personnel changes" (which disambiguates namesakes); players not on that page
are looked up as "<name> (cricketer)" then "<name>", and only pages with an
"Infobox cricketer" are used.

Writes data/source/research/player_styles.csv:
  ref, wiki_title, batting (raw), bowling (raw), hand (L/R), arm (L/R),
  style (fast | fast-medium | medium | finger | wrist | none), source
Unparseable rows keep blank hand/arm/style; build_dataset.py then falls back.
"""
from __future__ import annotations

import argparse
import csv
import os
import re
import time
import urllib.parse
import urllib.request

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
REF = os.path.join(ROOT, 'data', 'source', 'players_reference.csv')
OUT = os.path.join(ROOT, 'data', 'source', 'research', 'player_styles.csv')
CACHE = os.path.join(ROOT, 'build', 'wiki')
LIST = 'List_of_2026_Indian_Premier_League_personnel_changes'
UA = {'User-Agent': 'APL-data-builder/1.0 (research script)'}
# Our real_name -> Wikipedia's spelling, where they differ.
ALIASES = {
    'abishek porel': 'Abhishek Porel', 'harnoor singh pannu': 'Harnoor Pannu', 'vaibhav suryavanshi': 'Vaibhav Sooryavanshi',
    'yudhvir singh charak': 'Yudhvir Singh', 'prithvi raj yarra': 'Prithvi Raj', 'pravin dubey': 'Praveen Dubey',
    'sahil parakh': 'Sahil Parekh', 'vyshak vijaykumar': 'Vijaykumar Vyshak', 'rasikh dar': 'Rasikh Salam',
}


# Exact article titles where the name alone finds the wrong page.
TITLES = {'r312': 'Nathan Smith (New Zealand cricketer)', 'r328': 'Will Sutherland', 'r287': 'Kumar Kartikeya',
          'r291': 'Daryl Mitchell (New Zealand cricketer)'}


def norm(s: str) -> str:
    return re.sub(r'\s+', ' ', s.lower().replace('.', ' ').replace('-', ' ')).strip()


def fetch(title: str, refresh: bool) -> str | None:
    os.makedirs(CACHE, exist_ok=True)
    path = os.path.join(CACHE, re.sub(r'[^A-Za-z0-9_().-]', '_', title) + '.wiki')
    if os.path.exists(path) and not refresh:
        with open(path, encoding='utf-8') as f:
            text = f.read()
        return text or None
    url = 'https://en.wikipedia.org/w/index.php?' + urllib.parse.urlencode({'title': title, 'action': 'raw'})
    text = ''
    for _ in range(3):  # follow redirects
        try:
            with urllib.request.urlopen(urllib.request.Request(url, headers=UA), timeout=30) as r:
                text = r.read().decode('utf-8')
        except Exception:
            text = ''
            break
        m = re.match(r'#REDIRECT\s*\[\[([^\]|#]+)', text, re.I)
        if not m:
            break
        url = 'https://en.wikipedia.org/w/index.php?' + urllib.parse.urlencode({'title': m.group(1), 'action': 'raw'})
    time.sleep(0.3)
    with open(path, 'w', encoding='utf-8') as f:
        f.write(text)
    return text or None


def infobox_field(text: str, key: str) -> str:
    m = re.search(r'^[ \t]*\|[ \t]*' + key + r'[ \t]*=[ \t]*(.*)$', text, re.M | re.I)
    if not m:
        return ''
    v = m.group(1)
    v = re.sub(r'<ref[^>]*/>|<ref.*?</ref>|<!--.*?-->', '', v)
    # List templates ({{ubl|Right-arm off break|leg break}}): keep the items, first one first.
    v = re.sub(r'\{\{\s*(?:ubl|unbulleted list|hlist|plainlist)\s*\|', '', v, flags=re.I)
    v = re.sub(r'\[\[(?:[^|\]]*\|)?([^\]]*)\]\]', r'\1', v)
    v = re.sub(r'\{\{[^}]*\}\}', '', v)
    v = v.replace('}}', '').replace('|', ', ')
    return v.strip()


def classify(batting: str, bowling: str):
    b = batting.lower()
    hand = 'L' if b.startswith('left') else 'R' if b.startswith('right') else ''
    w = bowling.lower()
    arm = 'L' if 'left' in w or 'slow left' in w else 'R' if 'right' in w else ''
    # A spinner who bowls both is listed main style first ("off break, leg break").
    def first(keys, text):
        found = [text.find(k) for k in keys if k in text]
        return min(found) if found else -1
    wrist_at = first(('leg break', 'leg-break', 'legbreak', 'googly', 'wrist', 'chinaman', 'unorthodox'), w)
    finger_at = first(('orthodox', 'off break', 'off-break', 'off spin', 'offbreak'), w.replace('unorthodox', '#'))
    if wrist_at >= 0 and (finger_at < 0 or wrist_at < finger_at):
        style = 'wrist'
    elif finger_at >= 0:
        style = 'finger'
    elif 'fast-medium' in w or 'fast medium' in w or 'medium-fast' in w or 'medium fast' in w:
        style = 'fast-medium'
    elif 'medium' in w:
        style = 'medium'
    elif 'fast' in w:
        style = 'fast'
    else:
        style = ''
    if not arm and style == 'finger':
        # "Slow left-arm orthodox" says left; a plain "off break" is right-arm.
        arm = 'L' if 'orthodox' in w else 'R'
    if not arm and style == 'wrist':
        # "Leg break (googly)" is right-arm; left-arm wrist spin is "left-arm unorthodox".
        arm = 'L' if 'unorthodox' in w or 'chinaman' in w else 'R'
    return hand, arm, style


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--fetch', action='store_true')
    ap.add_argument('--refresh', action='store_true')
    a = ap.parse_args()

    listing = fetch(LIST, a.refresh) or ''
    titles = {}
    for first, last, rest in re.findall(r'\{\{sortname\|([^|}]*)\|([^|}]*)((?:\|[^}]*)?)\}\}', listing):
        dab = re.search(r'dab=([^|}]*)', rest)
        name = f'{first} {last}'.strip()
        titles[norm(name)] = f'{name} ({dab.group(1)})' if dab else name

    with open(REF, encoding='utf-8-sig', newline='') as f:
        refs = list(csv.DictReader(f))
    rows = []
    for r in refs:
        real = ALIASES.get(norm(r['real_name']), r['real_name'])
        tries = [TITLES[r['ref']]] if r['ref'] in TITLES else []
        tries += [titles[norm(real)]] if norm(real) in titles else []
        tries += [f'{real} (cricketer)', real]
        got = ('', '')
        for t in tries:
            text = fetch(t, a.refresh) if (a.fetch or os.path.exists(os.path.join(CACHE, re.sub(r'[^A-Za-z0-9_().-]', '_', t) + '.wiki'))) else None
            if text and re.search(r'\{\{\s*Infobox cricketer', text, re.I):
                got = (t, text)
                break
        title, text = got
        batting = infobox_field(text, 'batting') if text else ''
        bowling = infobox_field(text, 'bowling') if text else ''
        hand, arm, style = classify(batting, bowling)
        rows.append({'ref': r['ref'], 'wiki_title': title, 'batting': batting, 'bowling': bowling,
                     'hand': hand, 'arm': arm, 'style': style,
                     'source': 'https://en.wikipedia.org/wiki/' + urllib.parse.quote(title.replace(' ', '_')) if title else ''})
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, list(rows[0].keys()), lineterminator='\n')
        w.writeheader()
        w.writerows(rows)
    print(f'{len(rows)} rows -> {os.path.relpath(OUT, ROOT)}; no page: {sum(1 for x in rows if not x["wiki_title"])}; '
          f'no bowling style: {sum(1 for x in rows if x["wiki_title"] and not x["style"])}')


if __name__ == '__main__':
    main()
