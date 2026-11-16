"""Builds the publishable dataset from the source sheet (Python port of
packages/auction_core/tool/build_dataset.dart, which needs a Dart SDK we no
longer have).

    python tools/data/build_dataset.py [--version N] [--check] [--out PATH]

Reads  data/source/players_reference.csv + data/source/league.json
       (+ data/source/research/research_players.csv for market values)
Writes data/apl_dataset.json and client-src/Assets/Resources/Data/apl_dataset.json

--check   build in memory and compare field-for-field (and byte-for-byte)
          with the current data/apl_dataset.json; writes nothing.

The reference sheet holds real names and real prices for research only. They
are NEVER written out: every exported player uses `game_name`, and the build
fails if a game_name is missing, equals any real name, or is fewer than 3
edits from its own real name.

Port notes (to stay bit-identical with the Dart builder):
  * Dart's double.round() is half-away-from-zero; Python's round() is
    banker's. `dround` below is the Dart one.
  * fairPrice uses x^1.75 as x*sqrt(x)*sqrt(sqrt(x)) exactly as Dart/C#.
  * JSON is 2-space indented, non-ASCII unescaped, no trailing newline,
    doubles keep their ".0" (rating scale).
"""
from __future__ import annotations

import argparse
import csv
import io
import json
import math
import os
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
SOURCE = os.path.join(ROOT, 'data', 'source', 'players_reference.csv')
LEAGUE = os.path.join(ROOT, 'data', 'source', 'league.json')
RESEARCH = os.path.join(ROOT, 'data', 'source', 'research', 'research_players.csv')
OUT = os.path.join(ROOT, 'data', 'apl_dataset.json')
CLIENT_COPY = os.path.join(ROOT, 'client-src', 'Assets', 'Resources', 'Data', 'apl_dataset.json')
OUT_DEFAULT = OUT


def set_root(root: str) -> None:
    global ROOT, SOURCE, LEAGUE, RESEARCH, OUT, CLIENT_COPY
    ROOT = root
    SOURCE = os.path.join(ROOT, 'data', 'source', 'players_reference.csv')
    LEAGUE = os.path.join(ROOT, 'data', 'source', 'league.json')
    RESEARCH = os.path.join(ROOT, 'data', 'source', 'research', 'research_players.csv')
    OUT = os.path.join(ROOT, 'data', 'apl_dataset.json')
    CLIENT_COPY = os.path.join(ROOT, 'client-src', 'Assets', 'Resources', 'Data', 'apl_dataset.json')


MARQUEE_COUNT = 12
MIN_NAME_DISTANCE = 3
STAR_PREMIUM = 0.8

ROLES = {'BAT', 'BOWL', 'AR', 'WK'}


def dround(x: float) -> int:
    """Dart's round(): half away from zero."""
    return int(math.floor(x + 0.5)) if x >= 0 else -int(math.floor(-x + 0.5))


def edit_distance(a: str, b: str) -> int:
    prev = list(range(len(b) + 1))
    for i in range(1, len(a) + 1):
        cur = [i] + [0] * len(b)
        for j in range(1, len(b) + 1):
            cost = 0 if a[i - 1] == b[j - 1] else 1
            cur[j] = min(prev[j] + 1, cur[j - 1] + 1, prev[j - 1] + cost)
        prev = cur
    return prev[len(b)]


def read_csv(path: str) -> list[dict[str, str]]:
    with open(path, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.reader(f))
    header = rows[0]
    out = []
    for r in rows[1:]:
        if not any(c.strip() for c in r):
            continue
        out.append({h: (r[i].strip() if i < len(r) else '') for i, h in enumerate(header)})
    return out


# ---------------------------------------------------------------- player model

def impact(p: dict) -> int:
    bat, bowl = p['batting'], p['bowling']
    hi, lo = max(bat, bowl), min(bat, bowl)
    role = p['role']
    if role == 'BAT':
        raw = float(bat)
    elif role == 'BOWL':
        raw = float(bowl)
    elif role == 'AR':
        raw = 0.55 * hi + 0.45 * lo + 4
    else:
        raw = 0.85 * bat + 0.15 * p['keeping']
    return max(0, min(100, dround(raw + p['form'])))


def fair_price(p: dict, purse: int) -> float:
    if p.get('value', 0) > 0:
        return max(float(p['basePrice']), p['value'] * purse / 12000)
    a = float(max(0, impact(p) - 55))
    raw = 2.5 * (a * math.sqrt(a) * math.sqrt(math.sqrt(a))) * purse / 12000
    with_star = raw * (1 + STAR_PREMIUM * p.get('starPower', 0) / 100)
    return max(float(p['basePrice']), with_star)


def derived_base(p: dict) -> int:
    i = impact(p)
    if not p['capped']:
        return 50 if i >= 72 else (40 if i >= 66 else 30)
    if i >= 84:
        return 200
    if i >= 78:
        return 150
    if i >= 72:
        return 100
    if i >= 66:
        return 75
    return 50


def set_for(p: dict) -> str:
    if not p['capped']:
        return 'uncapped'
    return {'BAT': 'batters', 'BOWL': 'bowlers', 'AR': 'allRounders', 'WK': 'wicketKeepers'}[p['role']]


def form_factor(p: dict, r: dict) -> float:
    """Last season against par (28 runs a game at SR 150; 1.1 wickets a game at
    economy 8.8), square-rooted and held to 0.8-1.3. <5 games says nothing."""
    def n(key):
        try:
            return float(r.get(key, ''))
        except ValueError:
            return None
    games = n('t20_matches')
    if games is None:
        games = 14.0
    if games < 5:
        return 1.0
    runs, wickets = n('runs'), n('wickets')
    sr, econ = n('strike_rate'), n('economy')
    bat = None if runs is None else (runs / games / 28) * ((sr if sr is not None else 145) / 150)
    bowl = None if wickets is None else (wickets / games / 1.1) * (8.8 / (econ if econ is not None else 8.8))
    role = p['role']
    if role in ('BAT', 'WK'):
        index = bat
    elif role == 'BOWL':
        index = bowl
    else:
        vals = [v for v in (bat, bowl) if v is not None]
        index = max(vals) if vals else None
    if index is None:
        return 1.0
    return min(1.3, max(0.8, math.sqrt(index)))


# ---------------------------------------------------------------- build

def build(version: int, placeholders: bool = False, fielding: bool = False) -> dict:
    records = read_csv(SOURCE)
    problems: list[str] = []

    missing = [r['ref'] for r in records if not r['game_name']]
    if missing and not placeholders:
        problems.append(f'{len(missing)} players have no game_name (first: {", ".join(missing[:5])})')
    real_names = {r['real_name'].lower() for r in records}
    seen: dict[str, str] = {}
    for r in records:
        g = r['game_name'].lower()
        if not g:
            continue
        if g in real_names:
            problems.append(f"{r['ref']}: game_name is identical to a real name")
        d = edit_distance(g, r['real_name'].lower())
        if d < MIN_NAME_DISTANCE:
            problems.append(f"{r['ref']}: game_name \"{r['game_name']}\" is only {d} edits from the real name (need {MIN_NAME_DISTANCE})")
        if g in seen:
            problems.append(f"{r['ref']}: game_name duplicates {seen[g]}")
        seen[g] = r['ref']

    with open(LEAGUE, encoding='utf-8') as f:
        league = json.load(f)
    franchise_for = {f['sourceTeam']: f['id'] for f in league['franchises'] if f.get('sourceTeam')}
    mega_purse = league['rules']['formats']['mega']['purse']

    drafts = []
    for r in records:
        try:
            drafts.append(player_from_row(r, placeholders, franchise_for))
        except ValueError as e:
            problems.append(f"{r['ref']}: {e}")
    if problems:
        sys.stderr.write('Build failed:\n  ' + '\n  '.join(problems) + '\n')
        sys.exit(1)

    stats = read_stats()
    if stats:
        apply_ratings(drafts, records, stats)
        if fielding:
            apply_fielding(drafts, stats)
    values = market_values(records, drafts, mega_purse)
    for p in drafts:
        p['value'] = values[p['id']]

    pinned = {r['ref'] for r in records if r.get('marquee') == 'Y'}
    capped = sorted((p for p in drafts if p['capped'] and p['id'] not in pinned),
                    key=lambda p: -p['value'])  # stable, like Dart's List.sort on ties? see note
    marquee = set(pinned) | {p['id'] for p in capped[:MARQUEE_COUNT - len(pinned)]}
    for p in drafts:
        p['set'] = 'marquee' if p['id'] in marquee else set_for(p)

    dataset = {
        'version': version,
        'league': {'name': league['league']['name'], 'shortName': league['league']['shortName']},
        'franchises': [
            {k: f[k] for k in ('id', 'name', 'shortName', 'city', 'primaryColor', 'secondaryColor')}
            for f in league['franchises']
        ],
        'players': [player_json(p) for p in drafts],
        'rules': rules_json(league['rules']),
    }
    v = validate(dataset)
    if v:
        sys.stderr.write('Dataset invalid:\n  ' + '\n  '.join(v) + '\n')
        sys.exit(1)
    return dataset


def player_from_row(r: dict, placeholders: bool, franchise_for: dict) -> dict:
    def skill(key):
        try:
            v = int(r.get(key, ''))
        except ValueError:
            v = None
        if v is None or v < 0 or v > 100:
            raise ValueError(f'{key} must be 0-100, got "{r.get(key)}"')
        return v

    real_team = r['real_team']
    previous = franchise_for.get(real_team) if real_team else None
    if real_team and previous is None:
        raise ValueError(f'real_team "{real_team}" has no franchise with that sourceTeam')
    # Withdrawn-and-replaced (W) and released (X) players end the season in
    # the pool: their real_team is kept for the record only.
    if r.get('status') in ('W', 'X'):
        previous = None
    role = r['role']
    if role not in ROLES:
        raise ValueError(f'Unknown role "{role}"')
    keeping = skill('keep')
    # Optional per-player fielding (0-100). Absent = the old rule: keepers
    # field at their keeping skill, everyone else 70.
    field_raw = r.get('field', '')
    fielding = skill('field') if field_raw else (keeping if role == 'WK' else 70)
    bowl = r.get('bowl') or ''
    p = {
        'id': r['ref'],
        'name': r['game_name'] if r['game_name'] else f"Player {r['ref']}",
        'country': r['country'],
        'overseas': r['country'] != 'India',
        'capped': r['capped'] == 'Y',
        'role': role,
        'leftHanded': r['hand'] == 'L',
        'bowlType': bowl if bowl in ('pace', 'spin') else 'none',
        'batting': skill('bat'),
        'bowling': skill('bowlskill'),
        'fielding': fielding,
        'keeping': keeping,
        'form': 0,
        'basePrice': 30,
        'set': 'uncapped',
        'tags': [t for t in r['tags'].split('|') if t],
        'starPower': skill('star') if r.get('star') else 0,
        'previousTeam': previous,
        'value': 0,
        'bowlArm': r.get('arm') or None,
        'bowlStyle': r.get('style') or None,
        'recent': None,
    }
    p['basePrice'] = derived_base(p) if not r['base'] else int(r['base'])
    return p


# ---------------------------------------------------------------- ratings
# docs/RATINGS.md explains every constant here. Inputs: the sheet's hand
# ratings (bat / bowlskill = the PRIOR) and data/source/stats/player_stats.csv
# (Cricsheet ball-by-ball, 2025-01-01 on). Absent stats file = old behaviour.

SEASON_WEIGHT = {'ipl26': 1.0, 'ipl25': 0.5, 'other': 0.25}
BAT_QUALIFY, BOWL_QUALIFY = 200, 180        # weighted balls faced / bowled to set the scale
BAT_K, BAT_K_TAIL, BOWL_K = 180, 360, 240   # credibility: n / (n + K)
MAX_UP, MAX_DOWN = 10, 10                   # most a rating moves from the prior
PART_TIMER_CAP = 69                         # BAT/WK bowling never above this
FORM_MIN_BALLS = 60
BOWL_ECON_W = 0.55                          # bowling index: economy share (strike rate gets the rest)
FORM_STEP, FORM_CAP = 10, 3                 # form = 2026 gap to the longer-term level / 10 (half the old /5), held to +-3
LONG_TERM = ('ipl25', 'other')              # the longer-term level form is measured against
BLOCKS = ('ipl26', 'ipl25', 'other')


def read_stats() -> dict:
    path = os.path.join(ROOT, 'data', 'source', 'stats', 'player_stats.csv')
    if not os.path.exists(path):
        return {}
    with open(path, encoding='utf-8', newline='') as f:
        return {r['ref']: r for r in csv.DictReader(f) if r.get('cricsheet_id')}


def _block(r: dict, blk: str, k: str) -> float:
    if blk == 'other':
        return float(r[f't20_{k}']) - float(r[f'ipl26_{k}']) - float(r[f'ipl25_{k}'])
    return float(r[f'{blk}_{k}'])


def season_pars(stats: dict) -> dict:
    """Each block's par from every dataset player in it: batting average and
    strike rate, economy and balls per wicket (so a 9.5 economy in a
    high-scoring IPL counts the same as 7.5 in a low-scoring league)."""
    par = {}
    for b in BLOCKS:
        t = {k: sum(max(0.0, _block(r, b, k)) for r in stats.values()) for k in ('runs', 'balls', 'outs', 'bb', 'rc', 'wk')}
        par[b] = {'avg': t['runs'] / t['outs'], 'sr': 100 * t['runs'] / t['balls'],
                  'econ': 6 * t['rc'] / t['bb'], 'bpw': t['bb'] / t['wk']}
    return par


def _clip(x: float, lo: float, hi: float) -> float:
    """Ratios to par are held to a band so one freak season (4 wickets in 13 games) can't dominate."""
    return max(lo, min(hi, x))


def bat_index(r: dict, par: dict, blocks=BLOCKS):
    """Season-weighted mean of 0.4 ln(avg/par) + 0.6 ln(SR/par) (ratios clipped); weight = season weight x balls."""
    num = den = 0.0
    for b in blocks:
        runs, balls, outs = _block(r, b, 'runs'), _block(r, b, 'balls'), _block(r, b, 'outs')
        if balls <= 0:
            continue
        avg = min(60.0, max(runs, 1.0) / max(outs, 1.0))
        sr = 100.0 * max(runs, 1.0) / balls
        idx = 0.4 * math.log(_clip(avg / par[b]['avg'], 0.4, 2.0)) + 0.6 * math.log(_clip(sr / par[b]['sr'], 0.6, 1.6))
        w = SEASON_WEIGHT[b] * balls
        num += w * idx
        den += w
    return (num / den, den) if den else (None, 0.0)


def bowl_index(r: dict, par: dict, blocks=BLOCKS):
    """Season-weighted mean of -0.65 ln(econ/par) - 0.35 ln(balls per wicket/par) (ratios clipped):
    economy first, as in T20 (wickets are the noisier half)."""
    num = den = 0.0
    for b in blocks:
        bb, rc, wk = _block(r, b, 'bb'), _block(r, b, 'rc'), _block(r, b, 'wk')
        if bb <= 0:
            continue
        econ = max(3.0, 6.0 * rc / bb)
        bpw = bb / max(wk, 0.5)
        idx = -BOWL_ECON_W * math.log(_clip(econ / par[b]['econ'], 0.7, 1.4)) - (1 - BOWL_ECON_W) * math.log(_clip(bpw / par[b]['bpw'], 0.5, 2.0))
        w = SEASON_WEIGHT[b] * bb
        num += w * idx
        den += w
    return (num / den, den) if den else (None, 0.0)


def _scale(pairs):
    """(index, prior) of qualified players -> f(index) with the priors' mean and SD."""
    xs = [x for x, _ in pairs]
    ys = [y for _, y in pairs]
    mx, my = sum(xs) / len(xs), sum(ys) / len(ys)
    sx = math.sqrt(sum((x - mx) ** 2 for x in xs) / len(xs)) or 1.0
    sy = math.sqrt(sum((y - my) ** 2 for y in ys) / len(ys))
    return lambda x: my + sy * (x - mx) / sx


def apply_ratings(drafts, records, stats) -> None:
    par = season_pars(stats)
    BI = {i: bat_index(s, par) for i, s in stats.items()}
    WI = {i: bowl_index(s, par) for i, s in stats.items()}
    bat_fit = _scale([(BI[p['id']][0], p['batting']) for p in drafts if p['id'] in BI
                      and BI[p['id']][1] >= BAT_QUALIFY and p['role'] in ('BAT', 'WK', 'AR')])
    bowl_fit = _scale([(WI[p['id']][0], p['bowling']) for p in drafts if p['id'] in WI
                       and WI[p['id']][1] >= BOWL_QUALIFY and p['role'] in ('BOWL', 'AR')])

    def blend(prior, stat, n, k):
        v = prior + n / (n + k) * (stat - prior)
        return int(max(5, min(97, max(prior - MAX_DOWN, min(prior + MAX_UP, dround(v))))))

    for p in drafts:
        s = stats.get(p['id'])
        if not s:
            continue
        prior_bat, prior_bowl = p['batting'], p['bowling']
        bi, bn = BI[p['id']]
        if bi is not None:
            p['batting'] = blend(p['batting'], bat_fit(bi), bn, BAT_K_TAIL if p['role'] == 'BOWL' else BAT_K)
        wi, wn = WI[p['id']]
        if wi is not None and p['bowling'] >= 25:
            p['bowling'] = blend(p['bowling'], bowl_fit(wi), wn, BOWL_K)
            if p['role'] in ('BAT', 'WK'):
                p['bowling'] = min(p['bowling'], PART_TIMER_CAP)
        # Form: how IPL 2026 alone deviates from his LONGER-TERM level (prior blended with IPL 2025
        # and other T20s, 2026 left out), at half strength: the base rating already carries 2026
        # at full weight, so form adds only half of the same signal (steps of 10 points, held to +-3).
        p['prior_bat'], p['prior_bowl'] = prior_bat, prior_bowl
        gaps = []
        b26, n26 = bat_index(s, par, ('ipl26',))
        bl, nl = bat_index(s, par, LONG_TERM)
        if p['role'] in ('BAT', 'WK', 'AR') and b26 is not None and n26 >= FORM_MIN_BALLS:
            level = blend(prior_bat, bat_fit(bl), nl, BAT_K) if bl is not None else prior_bat
            gaps.append(bat_fit(b26) - level)
        w26, m26 = bowl_index(s, par, ('ipl26',))
        wl, ml = bowl_index(s, par, LONG_TERM)
        if p['role'] in ('BOWL', 'AR') and w26 is not None and m26 >= FORM_MIN_BALLS:
            level = blend(prior_bowl, bowl_fit(wl), ml, BOWL_K) if wl is not None else prior_bowl
            gaps.append(bowl_fit(w26) - level)
        if gaps:
            p['form'] = max(-FORM_CAP, min(FORM_CAP, dround(max(gaps) / FORM_STEP)))
        p['recent'] = recent_line(s)


# ---------------------------------------------------------------- fielding
# On since v5 (PM-approved: run-outs 0.77 -> 0.76 an innings with it). --no-fielding
# gives the old flat 70 for every non-keeper. See RATINGS.md.
FIELD_K = 25            # credibility: weighted matches / (matches + K)
FIELD_RO_WEIGHT = 1.5   # a run-out involvement counts 1.5 catches
FIELD_MAX_MOVE = 10     # 60..80 around the old flat 70
FIELD_BASE = 70


def fielding_table(drafts, stats) -> dict:
    """ref -> (weighted matches, weighted catches, weighted run-outs, rate, ratio, rating) for non-keepers."""
    rows = {}
    for p in drafts:
        s = stats.get(p['id'])
        if not s or p['role'] == 'WK':
            continue
        m = sum(SEASON_WEIGHT[b] * max(0.0, _block(s, b, 'm')) for b in BLOCKS)
        ct = sum(SEASON_WEIGHT[b] * max(0.0, _block(s, b, 'ct')) for b in BLOCKS)
        ro = sum(SEASON_WEIGHT[b] * max(0.0, _block(s, b, 'ro')) for b in BLOCKS)
        if m > 0:
            rows[p['id']] = [m, ct, ro, (ct + FIELD_RO_WEIGHT * ro) / m]
    par = sum(r[1] + FIELD_RO_WEIGHT * r[2] for r in rows.values()) / sum(r[0] for r in rows.values())
    out = {}
    for i, (m, ct, ro, rate) in rows.items():
        ratio = _clip(rate / par, 0.4, 2.5)
        stat = FIELD_BASE + 10 * math.log2(ratio)
        v = FIELD_BASE + m / (m + FIELD_K) * (stat - FIELD_BASE)
        out[i] = (m, ct, ro, rate, ratio, int(max(FIELD_BASE - FIELD_MAX_MOVE, min(FIELD_BASE + FIELD_MAX_MOVE, dround(v)))))
    return out


def apply_fielding(drafts, stats) -> None:
    table = fielding_table(drafts, stats)
    for p in drafts:
        if p['id'] in table:
            p['fielding'] = table[p['id']][5]


def recent_line(s: dict):
    for blk, label in (('ipl26', 'IPL 2026'), ('ipl25', 'IPL 2025'), ('t20', 'T20s 2025-26')):
        m = int(s[f'{blk}_m'])
        if m <= 0:
            continue
        runs, balls = int(s[f'{blk}_runs']), int(s[f'{blk}_balls'])
        bb, rc, wk = int(s[f'{blk}_bb']), int(s[f'{blk}_rc']), int(s[f'{blk}_wk'])
        return {'label': label, 'm': m, 'runs': runs,
                'sr': round(100.0 * runs / balls, 1) if balls else 0.0,
                'wkts': wk, 'econ': round(6.0 * rc / bb, 2) if bb else 0.0}
    return None


BREAKOUT_RISE = 6


def market_values(records, drafts, purse) -> dict:
    """Market value in lakhs at a 120 Cr purse - see the Dart original:
    latest real price (blended 80/20 with the older peak), tempered 25% toward
    skill value (60% after a breakout), times form and availability; unbought
    players are worth 60% of their skill value."""
    research = {}
    if os.path.exists(RESEARCH):
        with open(RESEARCH, encoding='utf-8-sig', newline='') as f:
            rows = list(csv.reader(f))
        head = rows[0]
        for r in rows[1:]:
            if len(r) < len(head):
                continue
            research[r[0]] = {h: r[i].strip() for i, h in enumerate(head)}
    by_id = {p['id']: p for p in drafts}
    real_price = {}
    for r in records:
        try:
            v = int(r.get('real_price', ''))
        except ValueError:
            continue
        if v > 0:
            real_price[r['ref']] = v

    ratios = sorted(v / fair_price(by_id[k], purse) for k, v in real_price.items())
    k = ratios[len(ratios) // 2] if ratios else 1.0

    def opt_int(s):
        try:
            return int(s)
        except (TypeError, ValueError):
            return None

    out = {}
    for p in drafts:
        skill_value = fair_price(p, purse) * k
        latest = real_price.get(p['id'])
        r = research.get(p['id'])
        if latest is None:
            value = skill_value * 0.6
        else:
            anchor = float(latest)
            old = []
            v24 = opt_int((r or {}).get('price_2024'))
            if v24 is not None:
                old.append(v24 * 1.2)
            v22 = opt_int((r or {}).get('price_2022'))
            if v22 is not None:
                old.append(v22 * 12 / 9)
            if old:
                anchor = 0.8 * anchor + 0.2 * max(old)
            form = 1.0 if r is None else form_factor(p, r)
            # Breakouts make an old cheap price stale. Research form factor (top ~50): lean 60% on
            # skills (the Dart rule). Stats lifting his rating BREAKOUT_RISE+ above the sheet's with
            # good 2026 form: value him on skills alone (a 2026 MVP on a 1.1 Cr contract is worth
            # what his game is worth now).
            rise = max(p['batting'] - p.get('prior_bat', p['batting']), p['bowling'] - p.get('prior_bowl', p['bowling']))
            stat_breakout = rise >= BREAKOUT_RISE and p['form'] >= 1
            if stat_breakout and skill_value > anchor:
                w = 1.0
            elif form >= 1.2 and skill_value > anchor:
                w = 0.6
            else:
                w = 0.25
            value = (1 - w) * anchor + w * skill_value
        if r is not None:
            try:
                avail = float(r.get('availability', ''))
            except ValueError:
                avail = 1.0
            value *= form_factor(p, r) * avail
        out[p['id']] = max(p['basePrice'], dround(value / 5) * 5)
    return out


def player_json(p: dict) -> dict:
    j = {k: p[k] for k in ('id', 'name', 'country', 'overseas', 'capped', 'role', 'leftHanded',
                           'bowlType', 'batting', 'bowling', 'fielding', 'keeping', 'form',
                           'basePrice', 'set', 'tags')}
    if p.get('image') is not None:
        j['image'] = p['image']
    if p['starPower'] != 0:
        j['starPower'] = p['starPower']
    if p['previousTeam'] is not None:
        j['previousTeam'] = p['previousTeam']
    if p['value'] != 0:
        j['value'] = p['value']
    if p.get('bowlArm'):
        j['bowlArm'] = p['bowlArm']
    if p.get('bowlStyle'):
        j['bowlStyle'] = p['bowlStyle']
    if p.get('recent'):
        j['recent'] = p['recent']
    return j


def retention_json(r: dict | None) -> dict:
    r = r or {}
    j = {
        'maxPlayers': r.get('maxPlayers', 3),
        'slabs': r.get('slabs', []) if 'maxPlayers' in r else [1800, 1400, 1100],
        'uncappedPrice': r.get('uncappedPrice', 0) if 'maxPlayers' in r else 400,
    }
    if r.get('atValue'):
        j['atValue'] = True
    return j


def rules_json(r: dict) -> dict:
    """Mirror of Dart AuctionRules.fromJson -> toJson."""
    formats = {}
    for fid, f in r['formats'].items():
        j = {k: f[k] for k in ('label', 'purse', 'minSquad', 'maxSquad', 'maxOverseas')}
        if f.get('maxLots') is not None:
            j['maxLots'] = f['maxLots']
        if f.get('rating') is not None:
            j['rating'] = {k: float(f['rating'][k]) for k in ('battingBase', 'bowlingBase', 'spread')}
        if f.get('retention') is not None:
            j['retention'] = retention_json(f['retention'])
        if f.get('retentionOnly'):
            j['retentionOnly'] = True
        formats[fid] = j
    rating = r.get('rating') or {'battingBase': 45, 'bowlingBase': 50, 'spread': 36}
    out = {
        'increments': [({'below': s['below']} if s.get('below') is not None else {}) | {'step': s['step']}
                       for s in r['increments']],
        'basePrices': r['basePrices'],
        'bidTimerSec': r['bidTimerSec'],
        'acceleratedTimerSec': r['acceleratedTimerSec'],
        'maxOverseasInXI': r['maxOverseasInXI'],
        'setOrder': r['setOrder'],
        'formats': formats,
        'rating': {k: float(rating[k]) for k in ('battingBase', 'bowlingBase', 'spread')},
        'retention': retention_json(r.get('retention')) if r.get('retention') else
        {'maxPlayers': 3, 'slabs': [1800, 1400, 1100], 'uncappedPrice': 400},
        'rtmCards': r.get('rtmCards', 1),
    }
    if r.get('maxPrice') is not None:
        out['maxPrice'] = r['maxPrice']
    if r.get('roomMaxPrice') is not None:
        out['roomMaxPrice'] = r['roomMaxPrice']
    return out


def validate(d: dict) -> list[str]:
    problems = []
    ids = set()
    base_prices = set(d['rules']['basePrices'])
    for p in d['players']:
        if p['id'] in ids:
            problems.append(f"Duplicate player id {p['id']}")
        ids.add(p['id'])
        if p['basePrice'] not in base_prices:
            problems.append(f"{p['id']}: base price {p['basePrice']} is not in basePrices")
        for s in (p['batting'], p['bowling'], p['fielding'], p['keeping']):
            if s < 0 or s > 100:
                problems.append(f"{p['id']}: skill out of 0-100")
    team_ids = {f['id'] for f in d['franchises']}
    for p in d['players']:
        if p.get('previousTeam') and p['previousTeam'] not in team_ids:
            problems.append(f"{p['id']}: previousTeam {p['previousTeam']} unknown")
    return problems


def dumps(d: dict) -> str:
    return json.dumps(d, indent=2, ensure_ascii=False)


def compare(new: dict, old: dict) -> list[str]:
    """Field-for-field differences, as readable lines."""
    diffs = []
    for key in ('version', 'league', 'franchises', 'rules'):
        if new.get(key) != old.get(key):
            diffs.append(f'{key}: differs')
    op = {p['id']: p for p in old['players']}
    np_ = {p['id']: p for p in new['players']}
    if [p['id'] for p in old['players']] != [p['id'] for p in new['players']]:
        diffs.append('player order/ids differ')
    for pid in sorted(set(op) | set(np_)):
        a, b = op.get(pid), np_.get(pid)
        if a is None or b is None:
            diffs.append(f'{pid}: only in {"new" if a is None else "old"}')
            continue
        for k in sorted(set(a) | set(b)):
            if a.get(k) != b.get(k):
                diffs.append(f'{pid}.{k}: old={a.get(k)!r} new={b.get(k)!r}')
        if list(a.keys()) != list(b.keys()):
            diffs.append(f'{pid}: key order differs')
    return diffs


def write_atomic(path: str, text: str) -> None:
    """Temp file, then rename over the target: a Unity build copying it never sees half a file."""
    tmp = path + '.tmp'
    with open(tmp, 'w', encoding='utf-8', newline='\n') as f:
        f.write(text)
    os.replace(tmp, path)


def write_fielding_csv(path: str) -> None:
    records = read_csv(SOURCE)
    with open(LEAGUE, encoding='utf-8') as f:
        league = json.load(f)
    franchise_for = {f['sourceTeam']: f['id'] for f in league['franchises'] if f.get('sourceTeam')}
    drafts = [player_from_row(r, False, franchise_for) for r in records]
    stats = read_stats()
    apply_ratings(drafts, records, stats)
    table = fielding_table(drafts, stats)
    with open(path, 'w', encoding='utf-8', newline='') as f:
        w = csv.writer(f, lineterminator='\n')
        w.writerow(['ref', 'name', 'role', 'w_matches', 'w_catches', 'w_runouts', 'rate', 'ratio_to_par', 'fielding'])
        for p in drafts:
            if p['id'] in table:
                m, ct, ro, rate, ratio, fld = table[p['id']]
                w.writerow([p['id'], p['name'], p['role'], round(m, 1), round(ct, 1), round(ro, 1), round(rate, 3), round(ratio, 2), fld])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--version', type=int, default=None,
                    help='dataset version (default: keep the current file\'s)')
    ap.add_argument('--check', action='store_true')
    ap.add_argument('--out', default=OUT_DEFAULT)
    ap.add_argument('--allow-placeholder-names', action='store_true')
    ap.add_argument('--no-fielding', dest='fielding', action='store_false',
                    help='keep non-keepers at the old flat 70 (Cricsheet fielding is on since v5, see RATINGS.md)')
    ap.add_argument('--fielding-csv', default=None, help='write the fielding table to this CSV')
    ap.add_argument('--root', default=None,
                    help='build from another checkout (e.g. the baseline snapshot) - use with --check')
    a = ap.parse_args()
    if a.root:
        set_root(os.path.abspath(a.root))
        if a.out == OUT_DEFAULT:
            a.out = OUT

    current = None
    if os.path.exists(OUT):
        with open(OUT, encoding='utf-8') as f:
            current_text = f.read()
        current = json.loads(current_text)
    version = a.version if a.version is not None else (current['version'] if current else 1)
    d = build(version, a.allow_placeholder_names, a.fielding)
    if a.fielding_csv:
        write_fielding_csv(a.fielding_csv)
    text = dumps(d)

    if a.check:
        diffs = compare(d, current)
        print(f'players: {len(d["players"])}  field diffs vs {os.path.relpath(OUT, ROOT)}: {len(diffs)}')
        for line in diffs[:200]:
            print('  ' + line)
        print('byte-identical:', text == current_text)
        sys.exit(0 if not diffs else 2)

    write_atomic(a.out, text)
    if os.path.abspath(a.out) == OUT:
        write_atomic(CLIENT_COPY, text)
    by_set = {}
    for p in d['players']:
        by_set[p['set']] = by_set.get(p['set'], 0) + 1
    print(f'Wrote {len(d["players"])} players (v{version}) to {a.out}' +
          (' (+ client Resources copy)' if os.path.abspath(a.out) == OUT else ''))
    print(f'  sets: {by_set}')


if __name__ == '__main__':
    main()
