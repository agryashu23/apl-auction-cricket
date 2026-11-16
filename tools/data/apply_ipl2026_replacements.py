"""One-off (idempotent) update of data/source/players_reference.csv to the
IPL 2026 squads as they ENDED the season: adds the mid-season replacement
signings, sets their team, and marks the one release. Every change is
logged with its source in data/source/research/ipl2026_squad_changes.csv.

    python tools/data/apply_ipl2026_replacements.py

Policy (PM decision 2026-11-12, see docs/RATINGS.md "Squads"):
  * a franchise's squad = its FINAL IPL 2026 squad: replacement signings are
    IN, the players they replaced go to the pool (no previousTeam), so every
    squad is exactly 25;
  * withdrawn players with NO replacement (Yash Dayal, Bethell, Marsh) stay;
  * Mustafizur Rahman was RELEASED by KKR on 3 Jan 2026 at the BCCI's
    instruction (not an injury): pool;
  * real_team keeps the 2026 team of record for everyone (research only);
    the builder gives no previousTeam to status W or X;
  * status column: R retained, B bought at the auction, P replacement
    signing, U unsold at the auction, W withdrawn and replaced, X released.
New players get avatar looks from tools/gen_looks.py (run it after this).
New players' ratings are provisional hand estimates (marked in notes) until
the formula in docs/RATINGS.md is applied to everyone.
"""
import csv
import os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
REF = os.path.join(ROOT, 'data', 'source', 'players_reference.csv')
LOG = os.path.join(ROOT, 'data', 'source', 'research', 'ipl2026_squad_changes.csv')

WIKI = 'https://en.wikipedia.org/wiki/List_of_2026_Indian_Premier_League_personnel_changes#Withdrawn_players'
IPL = 'https://www.iplt20.com/news/'

# Existing (unsold) players signed as replacements: ref -> (team, price in lakhs, replaced, source)
SIGNED = {
    'r260': ('SRH', 200, 'David Payne (ankle injury)', WIKI),
    'r261': ('CSK', 150, 'Nathan Ellis (hamstring)', WIKI),
    'r271': ('MI', 50, 'Quinton de Kock (wrist)', WIKI),
    'r277': ('MI', 30, 'Raj Angad Bawa (ligament tear)', WIKI),
    'r283': ('CSK', 30, 'Ayush Mhatre (hamstring)', WIKI),
    'r292': ('RR', 200, 'Sam Curran (groin)', WIKI),
    'r303': ('RCB', 160, 'Nuwan Thushara (NOC denied)', WIKI),
    'r309': ('GT', 75, 'Tom Banton (finger)', WIKI),
    'r323': ('CSK', 30, 'Ramakrishna Ghosh (foot fracture)', WIKI),
    'r329': ('SRH', 45, 'Shivam Mavi (groin)', WIKI),
}

# New players. Columns as players_reference.csv; ratings provisional.
NEW = [
    # ref, real, game, country, role, bowl, hand, bat, bowl, keep, capped, base, price, team, tags, notes, source
    ('r330', 'Blessing Muzarabani', 'Blessin Muzzaraabani', 'Zimbabwe', 'BOWL', 'pace', 'R', 15, 76, 0, 'Y', 75, 75, 'KKR', '',
     'replaced Mustafizur Rahman (released, BCCI request); right-arm fast-medium', WIKI + ' ; https://en.wikipedia.org/wiki/Blessing_Muzarabani'),
    ('r331', 'Krish Bhagat', 'Krissh Bhaggatt', 'India', 'BOWL', 'pace', 'R', 30, 60, 0, 'N', 30, 30, 'MI', '',
     'replaced Atharva Ankolekar (knee); right-arm medium, Punjab; batting hand not published', IPL + '4295/mumbai-indians-sign-krish-bhagat-as-a-replacement-for-atharva-ankolekar'),
    ('r332', 'Navdeep Saini', 'Navdip Saainee', 'India', 'BOWL', 'pace', 'R', 20, 72, 0, 'Y', 75, 75, 'KKR', '',
     'replaced Harshit Rana (knee); right-arm fast', WIKI + ' ; https://en.wikipedia.org/wiki/Navdeep_Saini'),
    ('r333', 'David Payne', 'Daivid Paaine', 'England', 'BOWL', 'pace', 'R', 20, 72, 0, 'Y', 150, 150, 'SRH', '',
     'replaced Jack Edwards (foot), then withdrew (ankle) for Gerald Coetzee; left-arm fast-medium', WIKI + ' ; https://en.wikipedia.org/wiki/David_Payne_(cricketer)'),
    ('r334', 'Saurabh Dubey', 'Sourabh Dubbay', 'India', 'BOWL', 'pace', 'R', 15, 64, 0, 'N', 30, 30, 'KKR', '',
     'replaced Akash Deep (back); left-arm fast, Vidarbha; batting hand not published', WIKI + ' ; https://en.wikipedia.org/wiki/Saurabh_Dubey_(Maharashtra_cricketer)'),
    ('r335', 'Rehan Ahmed', 'Rehaan Ahmmad', 'England', 'AR', 'spin', 'R', 58, 70, 0, 'Y', 75, 75, 'DC', '',
     'replaced Ben Duckett (personal reasons); leg-spin all-rounder', WIKI + ' ; https://en.wikipedia.org/wiki/Rehan_Ahmed'),
    ('r336', 'Kulwant Khejroliya', 'Kulwaant Khejrolyaa', 'India', 'BOWL', 'pace', 'L', 15, 66, 0, 'N', 30, 30, 'GT', '',
     'replaced Prithvi Raj Yarra (injury); left-arm medium-fast', WIKI + ' ; https://en.wikipedia.org/wiki/Kulwant_Khejroliya'),
    ('r337', 'George Linde', 'Jorge Lindey', 'South Africa', 'AR', 'spin', 'L', 62, 72, 0, 'Y', 100, 100, 'LSG', '',
     'replaced Wanindu Hasaranga (NOC denied); slow left-arm orthodox', WIKI + ' ; https://en.wikipedia.org/wiki/George_Linde'),
    ('r338', 'Dilshan Madushanka', 'Dilshaan Maddushankaa', 'Sri Lanka', 'BOWL', 'pace', 'R', 15, 74, 0, 'Y', 75, 75, 'SRH', '',
     'replaced Brydon Carse (injury); left-arm fast-medium', WIKI + ' ; https://en.wikipedia.org/wiki/Dilshan_Madushanka'),
    ('r339', 'Kuldip Yadav', 'Kuldipp Yaadaav', 'India', 'BOWL', 'pace', 'L', 15, 66, 0, 'N', 30, 30, 'CSK', '',
     'replaced Khaleel Ahmed (quadriceps); left-arm fast-medium', WIKI + ' ; https://en.wikipedia.org/wiki/Kuldip_Yadav'),
    ('r340', 'Keshav Maharaj', 'Keshaav Mahaaraaj', 'South Africa', 'BOWL', 'spin', 'R', 35, 78, 0, 'Y', 75, 75, 'MI', '',
     'replaced Mitchell Santner (shoulder); slow left-arm orthodox', WIKI + ' ; https://en.wikipedia.org/wiki/Keshav_Maharaj'),
    ('r341', 'Dian Forrester', 'Dyen Forrestar', 'South Africa', 'AR', 'pace', 'L', 64, 62, 0, 'Y', 75, 75, 'CSK', '',
     'replaced Jamie Overton (thigh); right-arm fast; T20I debut Mar 2026', WIKI + ' ; https://en.wikipedia.org/wiki/Dian_Forrester'),
    ('r342', 'Emanjot Chahal', 'Emmanjoth Chahaal', 'India', 'AR', 'spin', 'R', 52, 58, 0, 'N', 30, 30, 'RR', '',
     'replaced Ravi Singh (hamstring); left-arm spin all-rounder, Punjab; batting hand not published', IPL + '4368/rajasthan-royals-sign-emanjot-chahal-as-a-replacement-for-injured-ravi-singh'),
    ('r343', 'Luvnith Sisodia', 'Luvneeth Sissodiya', 'India', 'WK', 'none', 'L', 58, 5, 68, 'N', 30, 30, 'KKR', '',
     'replaced Matheesha Pathirana (hamstring); wicket-keeper batter, Karnataka', WIKI + ' ; https://en.wikipedia.org/wiki/Luvnith_Sisodia'),
]

# Replaced players -> (who replaced them, source); they move to the pool.
WITHDRAWN = {
    'r077': 'Navdeep Saini', 'r100': 'Saurabh Dubey', 'r011': 'Spencer Johnson', 'r187': 'Dasun Shanaka',
    'r249': 'David Payne', 'r146': 'Krish Bhagat', 'r046': 'Rehan Ahmed', 'r074': 'Kulwant Khejroliya',
    'r118': 'George Linde', 'r227': 'Dilshan Madushanka', 'r017': 'Kuldip Yadav', 'r066': 'Connor Esterhuizen',
    'r010': 'Akash Madhwal', 'r134': 'Keshav Maharaj', 'r247': 'RS Ambrish', 'r006': 'Macneil Noronha',
    'r003': 'Dian Forrester', 'r211': 'Richard Gleeson', 'r144': 'Mahipal Lomror', 'r140': 'Ruchit Ahir',
    'r192': 'Emanjot Chahal', 'r089': 'Luvnith Sisodia', 'r333': 'Gerald Coetzee',
}

RELEASED = {'r098': ('KKR released him on 3 Jan 2026 at the BCCI\'s instruction (not an injury); replaced by Blessing Muzarabani',
                     WIKI)}


def main():
    with open(REF, encoding='utf-8-sig', newline='') as f:
        rows = list(csv.DictReader(f))
    header = list(rows[0].keys())
    by_ref = {r['ref']: r for r in rows}
    log = []

    def change(r, field, new, src):
        old = r.get(field, '')
        if str(old) != str(new):
            log.append({'ref': r['ref'], 'player': r['real_name'], 'field': field, 'old': old, 'new': new, 'source': src})
            r[field] = str(new)

    for ref, (team, price, replaced, src) in SIGNED.items():
        r = by_ref[ref]
        change(r, 'real_team', team, src)
        change(r, 'real_price', price, src)
        change(r, 'status', 'P', src)
        note = f'IPL 2026 replacement for {replaced}'
        if note not in r['notes']:
            change(r, 'notes', (r['notes'] + '; ' if r['notes'] else '') + note, src)
    for ref, (why, src) in RELEASED.items():
        r = by_ref[ref]
        change(r, 'status', 'X', src)
        if 'released' not in r['notes']:
            change(r, 'notes', (r['notes'] + '; ' if r['notes'] else '') + why, src)
    for (ref, real, game, country, role, bowl, hand, bat, bowlskill, keep, capped, base, price, team, tags, notes, src) in NEW:
        if ref in by_ref:
            continue
        r = {h: '' for h in header}
        r.update({'ref': ref, 'real_name': real, 'game_name': game, 'country': country, 'role': role, 'bowl': bowl,
                  'hand': hand, 'bat': str(bat), 'bowlskill': str(bowlskill), 'keep': str(keep), 'capped': capped,
                  'base': str(base), 'real_price': str(price), 'real_team': team, 'status': 'P', 'tags': tags,
                  'notes': notes + '; ratings provisional (hand)', 'star': '0', 'marquee': ''})
        rows.append(r)
        by_ref[ref] = r
        log.append({'ref': ref, 'player': real, 'field': '(new player)', 'old': '', 'new': f'{team} replacement, {price}L', 'source': src})

    for ref, by in WITHDRAWN.items():
        r = by_ref[ref]
        change(r, 'status', 'W', WIKI)
        note = f'withdrawn from IPL 2026, replaced by {by}: pool'
        if 'replaced by' not in r['notes']:
            change(r, 'notes', (r['notes'] + '; ' if r['notes'] else '') + note, WIKI)

    with open(REF, 'w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, header, lineterminator='\n')
        w.writeheader()
        w.writerows(rows)

    exists = os.path.exists(LOG)
    with open(LOG, 'a', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, ['ref', 'player', 'field', 'old', 'new', 'source'], lineterminator='\n')
        if not exists:
            w.writeheader()
        w.writerows(log)
    print(f'{len(log)} changes logged to {os.path.relpath(LOG, ROOT)}; players now {len(rows)}')


if __name__ == '__main__':
    main()
