# Match mode — plan (v1: "Quick Match", you bat the chase)

Status: planned 2026-10-21; **step 1 (engine) done** — `packages/AuctionCore.Net/src/AuctionCore/Play/`
(Delivery, Bowling, Field, Batting, Chase), tests in `tests/AuctionCore.Tests/PlayTests.cs`.
Owner: founder. Built in the existing Unity app (`client-src/`).

Balance (bot batter, `PlayTests.Balance_…`): timing error in windows → result

| Timing | 20 overs | 5 overs |
|---|---|---|
| good (0.6) | 9.0 rpo, out every 39 balls, wins 43% vs ~182 | 10.0 rpo, wins 69% |
| average (1.2) | 7.6 rpo, out every 10 balls, wins 3% | 9.2 rpo, wins 34% |
| random (3.0) | 3.4 rpo, out every 4.6 balls | — |

## What the player gets (v1)

1. Title screen → **PLAY MATCH** (new, next to PLAY NOW / PLAY ONLINE). No auction needed.
2. Pick **your team** and the **opponent** from the 10 franchises. Squads = last season's real
   IPL squads (`previousTeam` in the dataset); the XI is picked automatically (best XI rules
   already in the core: keeper, 5 bowling options, max 4 overseas).
3. Pick **overs**: 2 · 5 · 10 · 20.
4. **Toss → the opponent bats first, simulated** (instant, with a scorecard reveal) using the
   existing ball-by-ball match engine. That sets your target.
5. **You bat the chase in 3D**: AI bowlers (real bowling plan: pace in powerplay, spin in
   the middle, death specialists at the end), you choose shots and time them.
6. Result screen: won/lost, your scorecard, the bowling figures, player of the match.

Bowling by the player, fielding control, replays and a full two-innings match come after v1.

## Controls (landscape, one thumb each side)

- **Left thumb — direction**: drag anywhere on the left half; an arrow on a mini field shows
  where you're aiming (8 zones: straight, cover, point, third man, fine leg, square leg,
  mid-wicket, long-on). Release = keep the last direction.
- **Right thumb — shot type**, tapped when the ball arrives:
  **DEFEND** · **GROUND** (keep it down) · **LOFT** (go aerial).
  The tap *is* the swing, so the moment you tap is your timing.
- **Timing meter** (the "slider"): a bar under the batter fills as the ball travels;
  the green zone is the ideal contact window. After each ball it shows EARLY / GOOD /
  PERFECT / LATE. The window is wider for better batters and narrower against faster
  or spinning deliveries.
- Do nothing = **leave** the ball (shoulder arms).
- Footwork is automatic from the length (front foot to full balls, back foot to short).

## How a ball is decided (the "simulation")

1. **Delivery** (AI bowler): type (pace / swing / seam / off-spin / leg-spin), speed from
   skill (pace 125–150 km/h, spin 80–95), **line** (outside off / off / middle / leg /
   wide), **length** (yorker / full / good / short / bouncer), swing or turn amount.
   Bowlers pick by phase and by the batter (more yorkers at the death, etc.), with
   accuracy from bowling skill — a poor bowler sprays some.
2. **Flight**: deterministic trajectory (gravity, drag, lateral swing in the air; bounce
   with pace-dependent energy loss; seam/spin deviation off the pitch) — so what you see is
   what the outcome uses.
3. **Contact quality** from: timing error vs the window · shot type vs length (lofting a
   yorker is hard, pulling a short ball is easy) · direction vs line (cutting a wide one
   works, flicking it doesn't) · batter vs bowler skill · pitch.
4. **Outcome**: miss (bowled / LBW if it was hitting the stumps) · edge (caught behind or
   runs to third man) · mistimed (catch chance, especially lofted) · clean hit →
   **power + direction** → ball path to the field → fielders (placed by the bowling side)
   run to cut it off → 0–4 along the ground, 6 or caught in the air.
5. Running between wickets is automatic (safe runs only in v1).

Balance target: a skilled human scores like a good T20 batter (8–10 an over) with real risk;
a bot that times randomly scores far less. Checked with a headless simulator before any
visuals (the same approach as the auction AI tuning).

## Build steps

| Step | What | Checks |
|---|---|---|
| **1. Engine** (pure C#, no visuals) | Deliveries, flight, timing, contact quality, outcome, field placement, scoring, innings/chase state, opponent's simulated innings | Unit tests + a headless "bot batter" simulator for scoring rates |
| **2. Ground** | New scene built from code like the hall: oval ground, pitch + creases + stumps, boundary rope, sight screens, stands with crowd, floodlights; broadcast camera (behind the bowler), batting camera, ball-follow camera | Screenshots |
| **3. Play** | Match menu (teams, overs), toss, simulated first innings card, chase HUD (score, target, required rate, balls left, ball speed, timing meter), controls, result screen | Scripted play-through on the Windows build |
| **4. People** | Batter, non-striker, bowler, keeper, umpire, 9 fielders with your clips; bat prop in the hands; fielders run, pick up, throw | Screenshots + play-through |
| **5. Polish** | Sounds (bat on ball, stumps, crowd), auctioneer-style voice lines via Polly ("FOUR!", "Bowled him!"), simple replay of wickets and sixes, then APK on request | — |

Steps 1–3 work with placeholder animations; your clips drop in at step 4.

## Motions needed (generate like the auction clips)

Standard humanoid bones (Hips, Spine, Neck, Head, LeftArm…), **in place** (no root travel unless
noted), start and end in the idle pose, keep head turns natural (under ~45°). For batting,
**both hands stay together on an imaginary bat handle** at the right hip — the bat prop is
attached in code. Right-handed; left-handers use a mirrored copy automatically.

**Batting (most important)**
1. `bat_stance_loop` — side-on batting stance, knees bent, tap the bat, slight bounce. Loop, 3 s.
2. `bat_defend_front` — big front-foot step, bat comes down straight and stops, head over the ball. 1.5 s.
3. `bat_defend_back` — step back and across, bat straight, soft hands. 1.5 s.
4. `bat_drive_cover` — front-foot drive to the off side, full follow-through. 1.8 s.
5. `bat_drive_straight` — front-foot straight drive, high finish. 1.8 s.
6. `bat_flick_leg` — front-foot flick / clip off the pads to the leg side, wrists rolling. 1.6 s.
7. `bat_cut` — back-foot square cut to the off side, bat horizontal. 1.6 s.
8. `bat_pull` — back-foot pull to the leg side, bat horizontal, rotating hips. 1.6 s.
9. `bat_loft_straight` — step out, big lofted hit over the bowler, full swing. 2 s.
10. `bat_slog_leg` — big slog / slog-sweep over mid-wicket, down on one knee is fine. 2 s.
11. `bat_leave` — lift the bat high, let the ball go past (shoulder arms). 1.5 s.
12. `bat_run` — running with the bat in the right hand (can be a run cycle in place). Loop, 1 s.

**Bowling**
13. `bowl_pace` — fast bowler's last strides, jump, delivery stride and follow-through (root may travel forward ~3 m). 2.5 s.
14. `bowl_spin` — spinner's short walk-in and delivery, arm over the top. 2 s.
15. `bowl_appeal` — bowler turns and appeals with both arms up. 1.5 s.

**Fielding, keeper, umpire**
16. `field_ready_loop` — fielder walking in / crouched ready. Loop, 2 s.
17. `field_pickup_throw` — bend, pick up the ball, overarm throw. 2 s.
18. `field_catch_high` — hands up high taking a catch, then celebrate. 2 s.
19. `keeper_stance_loop` — wicketkeeper crouched behind stumps, gloves together. Loop, 2 s.
20. `keeper_collect` — keeper rises and takes the ball at chest height. 1.2 s.
21. `umpire_out` — raise the right index finger. 1.5 s.
22. `umpire_four` — wave the arm side to side across the chest. 1.5 s.
23. `umpire_six` — both arms straight up. 1.5 s.

Priority order if you make them in batches: 1, 2, 4, 6, 9, 11 → 13, 14, 19 → the rest.
