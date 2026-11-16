"""Recent T20 stat lines for every dataset player, from Cricsheet open data.

    python tools/data/cricsheet_stats.py            # needs build/cricsheet (see below)
    python tools/data/cricsheet_stats.py --download # fetch the zips + registers first

Source: https://cricsheet.org (ball-by-ball JSON, Open Data Commons
Attribution licence). NOT Cricinfo. Downloads go to build/cricsheet/ (not
committed); the output is committed:

    data/source/stats/player_stats.csv   one row per dataset player

Columns (all from deliveries, so they are exact for the matches Cricsheet has):
  ipl25_* / ipl26_*   IPL 2025 and IPL 2026 seasons
  t20_*               every men's T20 in the window (IPL, T20Is, BBL, PSL,
                      SA20, CPL, ILT20, MLC, BPL, LPL, T20 Blast, SMAT)
                      from WINDOW_START on
  per block: m (matches in the XI), inn (batting innings), runs, balls,
  outs, fours, sixes, bb (balls bowled, legal), rc (runs conceded, excl.
  byes/leg-byes), wk (bowler's wickets), ct (catches, excl. as keeper),
  st (stumpings), ro (run-outs involved); Cricsheet doesn't flag the
  keeper, so ct includes a keeper's catches

Name matching (real names never leave data/source): the Cricsheet register
(names.csv) holds full names for most players; otherwise initials+surname,
narrowed by the player's IPL 2026 team. Every match is written with its
method, and unmatched players are listed.
"""
from __future__ import annotations

import argparse
import csv
import glob
import json
import os
import re
import sys
import unicodedata
import urllib.request
import zipfile
from collections import Counter, defaultdict

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
CACHE = os.path.join(ROOT, 'build', 'cricsheet')
REF = os.path.join(ROOT, 'data', 'source', 'players_reference.csv')
OUT = os.path.join(ROOT, 'data', 'source', 'stats', 'player_stats.csv')
WINDOW_START = '2025-01-01'
ZIPS = ['ipl_json', 't20s_male_json', 'bbl_json', 'psl_json', 'sat_json', 'cpl_json', 'ilt_json',
        'ntb_json', 'mlc_json', 'bpl_json', 'lpl_json', 'sma_json']
TEAM_NAMES = {
    'Chennai Super Kings': 'CSK', 'Delhi Capitals': 'DC', 'Gujarat Titans': 'GT',
    'Kolkata Knight Riders': 'KKR', 'Lucknow Super Giants': 'LSG', 'Mumbai Indians': 'MI',
    'Punjab Kings': 'PBKS', 'Rajasthan Royals': 'RR', 'Royal Challengers Bengaluru': 'RCB',
    'Royal Challengers Bangalore': 'RCB', 'Sunrisers Hyderabad': 'SRH',
}
FIELDS = ['m', 'inn', 'runs', 'balls', 'outs', 'fours', 'sixes', 'bb', 'rc', 'wk', 'ct', 'st', 'ro']
NOT_BOWLER_WICKETS = {'run out', 'retired hurt', 'retired out', 'obstructing the field', 'retired not out'}


def download():
    os.makedirs(CACHE, exist_ok=True)
    ua = {'User-Agent': 'APL-data-builder/1.0'}
    for z in ZIPS:
        path = os.path.join(CACHE, z + '.zip')
        req = urllib.request.Request(f'https://cricsheet.org/downloads/{z}.zip', headers=ua)
        with urllib.request.urlopen(req) as r, open(path, 'wb') as f:
            f.write(r.read())
        with zipfile.ZipFile(path) as zf:
            zf.extractall(os.path.join(CACHE, z))
        print('downloaded', z)
    for reg in ('people.csv', 'names.csv'):
        req = urllib.request.Request(f'https://cricsheet.org/register/{reg}', headers=ua)
        with urllib.request.urlopen(req) as r, open(os.path.join(CACHE, reg), 'wb') as f:
            f.write(r.read())


def fold(s: str) -> str:
    s = unicodedata.normalize('NFKD', s).encode('ascii', 'ignore').decode()
    return ' '.join(re.sub(r'[^a-z ]', ' ', s.lower()).split())


def new_block():
    return {k: 0 for k in FIELDS}


def scan():
    """stats[block][pid] -> counts; also who played for which IPL team."""
    stats = defaultdict(lambda: defaultdict(new_block))
    ipl_team_players = defaultdict(set)       # (season, team) -> {pid}
    short_name = {}                            # pid -> name as printed
    for z in ZIPS:
        for f in glob.glob(os.path.join(CACHE, z, '*.json')):
            with open(f, encoding='utf-8') as fh:
                j = json.load(fh)
            info = j['info']
            date = info['dates'][0]
            if date < WINDOW_START:
                continue
            if info.get('overs', 20) != 20 or info.get('gender') != 'male':
                continue
            reg = info['registry']['people']
            blocks = ['t20']
            if z == 'ipl_json':
                season = str(info.get('season', date[:4]))[:4]
                if season in ('2025', '2026'):
                    blocks.append('ipl' + season[2:])
            for team, players in info['players'].items():
                for name in players:
                    pid = reg[name]
                    short_name[pid] = name
                    for b in blocks:
                        stats[b][pid]['m'] += 1
                    if z == 'ipl_json' and len(blocks) > 1:
                        ipl_team_players[(blocks[1], TEAM_NAMES.get(team, team))].add(pid)
            for inn in j.get('innings', []):
                if inn.get('super_over'):
                    continue
                batted = set()
                for over in inn.get('overs', []):
                    for d in over['deliveries']:
                        bat = reg[d['batter']]
                        bowl = reg[d['bowler']]
                        ex = d.get('extras', {})
                        r = d['runs']
                        wide = 'wides' in ex
                        nb = 'noballs' in ex
                        for b in blocks:
                            s = stats[b]
                            if bat not in batted:
                                s[bat]['inn'] += 1
                            if not wide:
                                s[bat]['balls'] += 1
                            s[bat]['runs'] += r['batter']
                            if r['batter'] == 4 and not r.get('non_boundary'):
                                s[bat]['fours'] += 1
                            if r['batter'] == 6:
                                s[bat]['sixes'] += 1
                            if not wide and not nb:
                                s[bowl]['bb'] += 1
                            s[bowl]['rc'] += r['batter'] + ex.get('wides', 0) + ex.get('noballs', 0)
                        batted.add(bat)
                        for w in d.get('wickets', []):
                            out = reg.get(w['player_out'])
                            kind = w['kind']
                            for b in blocks:
                                s = stats[b]
                                if out and kind not in ('retired hurt', 'retired not out'):
                                    s[out]['outs'] += 1
                                if kind not in NOT_BOWLER_WICKETS:
                                    s[bowl]['wk'] += 1
                                for fl in w.get('fielders', []):
                                    fid = reg.get(fl.get('name', '')) if fl.get('name') else None
                                    if not fid or fl.get('substitute'):
                                        continue
                                    if kind == 'stumped':
                                        s[fid]['st'] += 1
                                    elif kind == 'caught':
                                        s[fid]['ct'] += 1
                                    elif kind == 'run out':
                                        s[fid]['ro'] += 1
                                if kind == 'caught and bowled':
                                    s[bowl]['ct'] += 1
    return stats, ipl_team_players, short_name


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--download', action='store_true')
    a = ap.parse_args()
    if a.download:
        download()
    if not os.path.isdir(os.path.join(CACHE, 'ipl_json')):
        sys.exit('No Cricsheet cache in build/cricsheet - run with --download')

    names = defaultdict(set)
    with open(os.path.join(CACHE, 'names.csv'), encoding='utf-8') as f:
        for r in csv.DictReader(f):
            names[fold(r['name'])].add(r['identifier'])
    with open(os.path.join(CACHE, 'people.csv'), encoding='utf-8') as f:
        for r in csv.DictReader(f):
            names[fold(r['name'])].add(r['identifier'])
            names[fold(r['unique_name'])].add(r['identifier'])

    stats, ipl_team, short = scan()
    ipl_any = {pid for ps in ipl_team.values() for pid in ps}
    candidates = {}
    active = set(stats['t20'])

    with open(REF, encoding='utf-8-sig', newline='') as f:
        refs = list(csv.DictReader(f))
    overrides = {}
    ov_path = os.path.join(ROOT, 'data', 'source', 'stats', 'name_overrides.csv')
    if os.path.exists(ov_path):
        with open(ov_path, encoding='utf-8') as f:
            for r in csv.DictReader(f):
                overrides[r['ref']] = r['cricsheet_id']

    rows, unmatched = [], []
    for r in refs:
        real = fold(r['real_name'])
        team = r['real_team'] or r.get('team_2026', '')
        team_pids = ipl_team.get(('ipl26', team), set()) | ipl_team.get(('ipl25', team), set())
        pid, how = None, ''
        if r['ref'] in overrides:
            pid, how = overrides[r['ref']], 'override'
            if pid == '-':  # checked by hand: the only candidates are other people
                pid, how = None, 'override:none'
        if not pid and how != 'override:none':
            cands = names.get(real, set())
            act = [c for c in cands if c in active]
            if len(cands) == 1:
                pid, how = next(iter(cands)), 'register'
            elif len(act) == 1:
                pid, how = act[0], 'register+active'
            elif act and team_pids & set(act):
                pid, how = sorted(team_pids & set(act))[0], 'register+team'
        if not pid and how != 'override:none':
            # initials + surname among the team's IPL players, then all active
            parts = real.split()
            sur, init = parts[-1], parts[0][0]
            def ok(p):
                n = fold(short.get(p, ''))
                return n.endswith(' ' + sur) and n.split()[0][0] == init
            def overlap(p):
                # "Varun Chakravarthy" ~ "CV Varun", "Sai Sudharsan" ~ "B Sai Sudharsan"
                toks = {t for t in fold(short.get(p, '')).split() if len(t) > 2}
                return bool(toks & set(parts))
            for pool, test, label in ((team_pids, ok, 'initials+team'), (team_pids, overlap, 'token+team'),
                                      (active, ok, 'initials+active')):
                hits = [p for p in pool if test(p)]
                if len(hits) > 1 and test is overlap:
                    # keep the candidates sharing the most name tokens
                    score = {p: len({t for t in fold(short.get(p, '')).split() if len(t) > 2} & set(parts)) for p in hits}
                    best = max(score.values())
                    hits = [p for p in hits if score[p] == best]
                if len(hits) == 1:
                    pid, how = hits[0], label
                    break
            if not pid:
                cand = sorted((p for p in active if ok(p) or overlap(p)), key=lambda p: -stats['t20'][p]['m'])[:6]
                candidates[r['ref']] = [(p, short.get(p), stats['t20'][p]['m']) for p in cand]
        row = {'ref': r['ref'], 'cricsheet_id': pid or '', 'cricsheet_name': short.get(pid, '') if pid else '',
               'match': how}
        for blk in ('ipl26', 'ipl25', 't20'):
            s = stats[blk].get(pid) if pid else None
            for k in FIELDS:
                row[f'{blk}_{k}'] = (s or {}).get(k, 0)
        rows.append(row)
        if not pid:
            unmatched.append(r['ref'])

    used = Counter(r['cricsheet_id'] for r in rows if r['cricsheet_id'])
    dupes = {k for k, v in used.items() if v > 1}
    if dupes:
        sys.exit('same Cricsheet player matched twice (add a name_overrides.csv row): ' +
                 ', '.join(f"{r['ref']}={r['cricsheet_id']}" for r in rows if r['cricsheet_id'] in dupes))
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    cols = list(rows[0].keys())
    with open(OUT, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, cols, lineterminator='\n')
        w.writeheader()
        w.writerows(rows)
    print('wrote', len(rows), 'rows ->', os.path.relpath(OUT, ROOT))
    print('match methods:', dict(Counter(r['match'] or 'UNMATCHED' for r in rows)))
    print('no recent T20 at all:', sum(1 for r in rows if r['cricsheet_id'] and r['t20_m'] == 0))
    if unmatched:
        print('unmatched (candidates: id, name, recent T20 matches):')
        for u in unmatched:
            print(' ', u, candidates.get(u))


if __name__ == '__main__':
    main()
