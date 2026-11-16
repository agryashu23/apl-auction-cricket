# Player data, squads and ratings (area 2)

How `data\apl_dataset.json` is made, and how every rating is computed. One command
rebuilds the game file from the committed CSVs:

```
python tools/data/build_dataset.py            # writes data\apl_dataset.json AND client-src\...\Resources\Data\apl_dataset.json
python tools/data/build_dataset.py --check    # rebuilds in memory, compares field-for-field and byte-for-byte
```

`build_dataset.py` is a Python port of `packages\auction_core\tool\build_dataset.dart` (no Dart SDK
here). Before any data change it reproduced the old 329-player v3 file byte-for-byte; that proof can be
re-run at any time against the frozen inputs in `build\review\deep\data_baseline`:

```
python tools/data/build_dataset.py --check --root build/review/deep/data_baseline
```

## Inputs (all committed under `data\source`)

| File | What | Made by |
|---|---|---|
| `players_reference.csv` | one row per player: real name (research only), `game_name`, role, `bowl` (pace/spin), `hand` (batting hand), `arm`, `style`, hand-rated `bat`/`bowlskill`/`keep` (the **prior**), capped, base, real price, `real_team`, `status`, star power | hand + the two apply scripts below |
| `research\ipl2026_squad_changes.csv` | every change made to the sheet: ref, player, field, old, new, **source URL** | the apply scripts |
| `research\player_styles.csv` | batting hand, bowling arm and style from each player's Wikipedia infobox | `tools/data/wiki_styles.py` |
| `stats\player_stats.csv` | recent T20 numbers per player, from Cricsheet ball-by-ball data | `tools/data/cricsheet_stats.py` |
| `stats\name_overrides.csv` | hand-checked Cricsheet matches (or "-" = nobody) | hand |
| `research\research_players.csv` | real prices 2022–26 and availability for the top ~50 (market values) | earlier research |

Real names never leave `data\source`: the build fails if a `game_name` is missing, equals any real
name, or is fewer than 3 edits (Levenshtein) from its own real name.

Full refresh, in order (each step is idempotent and logs its changes with a source):

```
python tools/data/apply_ipl2026_replacements.py   # squads as IPL 2026 ended (below)
python tools/gen_looks.py                         # avatar looks for new players
python tools/data/wiki_styles.py --fetch          # infobox hand/arm/style -> research\player_styles.csv
python tools/data/ipl_styles.py --fetch           # iplt20.com profiles for bowlers Wikipedia lacks
python tools/data/apply_styles.py                 # hand/arm/style/capped fixes into the sheet
python tools/data/cricsheet_stats.py --download   # Cricsheet stats -> stats\player_stats.csv
python tools/data/build_dataset.py --version 7
```

Sources: Wikipedia ("List of 2026 Indian Premier League personnel changes" and player infoboxes),
iplt20.com news pages, Wisden/myKhel (unsold list), Cricsheet (cricsheet.org, ODC-BY). No Cricinfo.

## Squads (`previousTeam`)

A franchise's squad is its **final IPL 2026 squad** (PM decision, 12 Nov 2026):
- the 173 retained and 77 auction buys (matched 250/250 against Wikipedia);
- **mid-season replacement signings are in** (24: 14 new players r330–r343, and 10 who were in our unsold pool);
- the players they replaced go to the **pool** (`status W`, no `previousTeam`), so every squad is exactly 25;
- withdrawn players with no replacement (Yash Dayal, Bethell, Marsh) stay;
- Mustafizur Rahman was released by KKR at the BCCI's instruction (`status X`): pool;
- the 79 unsold players (all on the Wisden/myKhel unsold list) have no `previousTeam`.

`real_team` keeps the 2026 team of record for everyone (research only). Status codes: R retained,
B bought, P replacement signing, U unsold, W withdrawn and replaced, X released.

## Ratings

The sheet's hand ratings (`bat`, `bowlskill`) are the **prior**. Where Cricsheet has the player
(265 of 343 have T20 matches since Jan 2025), the build moves each rating towards what his recent numbers say. Players with no recent
T20 data keep the prior unchanged. Keeping (`keep`) and star power are not stat-derived.

**Window and weights.** Every men's 20-over match from 1 Jan 2025: IPL 2026 (weight 1.0), IPL 2025
(0.5), every other T20 (internationals, BBL, PSL, SA20, CPL, ILT20, MLC, BPL, LPL, Blast; 0.25). v7
gave the last IPL more weight (was 1.0 / 0.6 / 0.5) so the 2026 season leads and a bowler with no
IPL 2026 game (Mustafizur) no longer ranks top 3 on other leagues alone.

**Season par.** Each block's par is computed from all dataset players in it (IPL 2026: average 29.8,
SR 156.4, economy 9.77, 20.0 balls a wicket; other T20s: 26.6, 147.8, 8.40, 17.6). Comparing to the
block's own par means a 9.5 economy in a high-scoring IPL counts like 7.5 in a low-scoring league.

**Batting index** (per block, then averaged weighting each block by season weight x balls faced):

    0.4 x ln(average / par average) + 0.6 x ln(strike rate / par SR)

average = runs / dismissals (min 1 dismissal, capped at 60); ratios held to [0.4, 2.0] and [0.6, 1.6].

**Bowling index** (same averaging, by balls bowled), economy slightly ahead of wickets:

    -0.55 x ln(economy / par economy) - 0.45 x ln(balls per wicket / par)

The split is a trade-off. 50/50 (v5) buried economy-first bowlers (Narine, Bumrah); 65/35 (v6) sank
the 2026 wicket-takers (Rabada, 29 wickets and the Purple Cap, effectively 19th). 55/45 with the v7
season weights keeps Bhuvneshwar and Narine top-5 and puts Rabada 7th and Archer 11th.

ratios held to [0.7, 1.4] and [0.5, 2.0]; 0 wickets counts as 0.5 (so one wicketless spell can't
dominate). Run-outs and retirements are not bowler wickets.

**Index to rating.** The index is put on the rating scale by matching the mean and spread of the
priors of qualified players (batting: BAT/WK/AR with 200+ weighted balls; bowling: BOWL/AR with
180+ weighted balls bowled). This keeps the league's rating distribution (and so match totals and
prices) where it was, while the order now follows the numbers.

**Blend with the prior** (credibility grows with sample size):

    rating = prior + n / (n + K) x (stat rating - prior)

K = 180 balls for batting (360 for BOWL-role tail-enders), 240 balls bowled for bowling. Then:
- caps: at most +10 / -10 from the prior; overall 5..97;
- part-timers: a BAT or WK player's bowling never above 69;
- a bowling rating under 25 (non-bowler) is not touched.

**Form** (-3..+3, added to skills in play and to Impact in the auction). v6 fixes the double count
the reviewer found (the base already weights IPL 2026 at 1.0): form is now how IPL 2026 alone deviates
from the player's **longer-term level** (his prior blended, as above, with IPL 2025 and other T20s
only, 2026 left out), at **half strength**: `round(gap / 10)` (it was gap / 5 against the 2026-inclusive
rating), held to -3..+3; needs 60+ balls in 2026 (BAT/WK use batting, BOWL bowling, all-rounders the
better of the two). Result (v7): 236 players 0, 62 at +-1, 30 at +-2, 15 at +-3. Bumrah's 2026 (4 wickets
in 13 games, a gap of over 25 points to his level) still gives -3: base 86 (the -10 cap), Impact 83.

**Market value** keeps the Dart method (`market_values` in the builder): latest real price
blended with skill value (25% skill; 60% for a research-flagged breakout). v6 adds a **stat breakout**
for every player: if the stats lifted his rating 6+ points above the sheet's and his 2026 form is +1
or better, his old price is stale and he is valued on skill alone. That takes the 2026 MVP
(Sooryavanshi, 1.1 Cr contract) from 11.85 Cr to 18.0 Cr (v7); he is a top-10 buy in 19 of 20 mega
seeds (about 19.2 Cr). Others lifted: Bosch, Kartik Tyagi, Ashwani Kumar, Sakib Hussain, Hinge (all
under 4 Cr). In the mini auction no uncapped player goes past 13.5 Cr, under the 14.2 Cr that IPL 2026
really paid for an uncapped player (Prashant Veer, Kartik Sharma).

**Card line.** `recent` = the player's most recent season on record (IPL 2026, else IPL 2025, else
all T20s 2025–26): matches, runs, strike rate, wickets, economy. The auction card shows it.

### Bowling arm and style
`bowlArm` (L/R) and `bowlStyle` (fast, fast-medium, medium, finger, wrist) come from the Wikipedia
infobox ("off break"/"slow left-arm orthodox" = finger, "leg break"/"googly"/"left-arm unorthodox" =
wrist; a spinner who bowls both is given the style listed first). The engine reads them
(`Bowling.LeftArm`, `KindFor`; a known medium-pacer's pace class is capped at medium) and falls back
to the old stable hash only when they're missing. Sources in order: Wikipedia infobox, the iplt20.com
player profile ("Bowling Style"), then hand-checked rows with their source (`MANUAL` in
`apply_styles.py`: also the Delhi Capitals profile, the BCCI auction-list row as quoted by search
(the PDF itself now returns 403, not worked around) and iplt20 news/video pages). 23 fringe uncapped
bowlers have none of these and keep the fallback (12 sit in 2026 squads without public profile data,
e.g. Daksh Kamra, Naman Tiwari, Vicky Ostwal; 11 are unsold).

### Fielding (on since v5)
Approved by the PM on 13 Nov 2026 once the impact (below) proved negligible. Keepers keep fielding =
keeping. `--no-fielding` builds the old flat 70 for every non-keeper; the per-player table is written with

    python tools/data/build_dataset.py --fielding-csv data/source/stats/fielding_ratings.csv

Per non-keeper, season-weighted as above: catches + 1.5 x run-out involvements per match, against the
dataset's par rate; `stat = 70 + 10 x log2(rate / par)` (ratio held to 0.4..2.5), blended
`70 + m / (m + 25) x (stat - 70)` with m = weighted matches, held to 60..80. Cricsheet has no
dropped-catch data, and catches also depend on where a man fields (slips, ring), so the spread is kept
narrow. Result: 232 players, 63..77 (best: Hetmyer, Brevis, Phillips, Rinku, Tilak; lowest: specialist
bowlers who field at fine leg). Engine impact (v4 without, v5 with): run-outs 0.77 -> 0.76 an innings
(RunOutChanceTests), RunsShape and 3-over totals unchanged (35.4 -> 35.3 AI mean).

## Checks

- `AuctionPriceTests` (engine tests), v7: 20 mega auctions average 3.8 players over 20 Cr (3–5),
  dearest 26.0 Cr (cap 28); the regular top-10 buys are all top or famous players. Standard: dearest
  13.5 Cr; no 30 L uncapped player valued under 8 Cr sells for 10 Cr+ (limit 0; the exempted, market-
  backed sales are printed); no uncapped player above 14.2 Cr; retention at value holds.
- `RulesTests.The_AI_batter_scores_like_a_t20_side_against_the_users_bowling` runs 200 innings (was
  60, whose +-0.9 standard error put it on its 18-ball edge with any data change); the bound is 17
  balls a wicket, two standard errors under the ~18 target (IPL 2026 par 20.0, other T20s 17.6).
  v7: 18.2.
- `Round5Tests` bouncer test picks the best genuinely fast bowler: the top-rated pacer is now
  Bhuvneshwar, whose medium style caps his pace class.
- `GoldenTests` read a frozen copy of the v3 dataset (`packages\AuctionCore.Net\golden\dataset.json`):
  the Dart goldens were exported on it, and there's no Dart SDK to re-export them.
