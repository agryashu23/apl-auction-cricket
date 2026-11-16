# APL — Auction Premier League · Spec v0.2

Android game, auction only: the user never plays cricket. Teams and players
are fictional. "APL — Auction Premier League" is the working name (run a
trademark check before launch: the Andhra Premier League also uses "APL").

## 1. Modes

| Mode | Who | Needs internet? |
|---|---|---|
| **Solo vs AI** | 1 human team, AI runs the other 9 | No (uses last downloaded player data) |
| **Friends room** | 2–10 humans join with a 6-letter room code; AI bots take every team nobody picked | Yes |

No public matchmaking in v1.

## 2. Game flow

1. Choose mode → choose your franchise (10 fictional teams, e.g. "Mumbai Heroes") → choose **auction type**:
   - **Fresh auction**: every player goes under the hammer.
   - **Retention**: before the auction each team keeps **up to 3** of last season's players. Capped players cost ₹18 / 14 / 11 Cr (most valuable first), uncapped ₹4 Cr, all scaled to the format's purse and deducted from it. You pick yours on a retention screen; AI teams keep players whose market value justifies the slab.
2. Choose auction length (values tuned in phase 1, all set on the server):

   | Format | Purse | Squad | Overseas | Lots | Squads short after the auction |
   |---|---|---|---|---|---|
   | **Quick** | ₹60 Cr | 11–15 | 5 | ~70 best players | auto-filled from the rest of the pool at base price |
   | **Standard** | ₹100 Cr | 15–20 | 6 | ~160 | auto-filled |
   | **Mega** | ₹120 Cr | 18–25 | 8 | whole pool | auto-filled |

3. **Auction**: Marquee first, then the role sets **rotate in tiers of 8**, like the real auction's BA1, AL1, WK1, FA1… (best 8 batters → best 8 bowlers → best 8 all-rounders → best 8 keepers → next 8 of each), then uncapped players in tiers. Rotation matters: with a fixed order, overseas slots ran out before the keeper set and every overseas keeper went unseen. Unsold players get one faster round at the end (best first, only as many as teams still need).
4. **Squad rating**: shown right after the auction ends (section 5).
5. **Simulated season**: instant results, points table, playoffs, awards (section 6).

## 3. Auction rules (all values set on the server)

- Base prices: ₹2 Cr / 1.5 / 1 / 0.75 / 0.5 / 0.3
- Bid increments: +₹10L below ₹1 Cr, +₹20L from ₹1–5 Cr, +₹25L above ₹5 Cr
- Bid timer: 10 s (6 s in the accelerated round), resets on every bid
- A team can't bid if winning would leave too little to fill its minimum squad at the lowest base price
- Every bid carries the amount the bidder saw; if someone else bid first, it's rejected as stale (no double-charging when two people tap at once)
- Max 4 overseas in the playing XI
- **Right to Match (RTM)**: every team gets **1 card** per auction, in both auction types. When the timer runs out on a player who was in your squad last season and another team has the winning bid, you're asked "Use RTM?"; yes = he joins you **at the winning price** and the outbid team pays nothing. The offer only appears if you can afford him and have squad/overseas room. AI teams save the card for their best former players. (The real IPL adds a final raise by the winning bidder; not in v1, easy to add.)
- **Pass**: drop out of the current lot; you can't bid on that player again. Solo: the lot settles instantly between AI teams. Friend room: the lot fast-forwards once every human has passed (never while a human is leading).
- **Hold to fast-forward**: while the button is held, the current lot runs at ~4× (AI thinking time and bid timer both shrink); releasing returns to normal pace.
- Solo only: **pause**
- Not in v1: trades

## 4. AI bidders

- Three personalities: **aggressive** (pays a premium for stars, bids fast), **balanced**, **value-hunter** (bargains, slower).
- Valuation = market price for the player's quality × how much the squad needs that role × personality × small random spread. Market prices rise and fall with how much money is left in the room versus the players left.
- Budget pacing over the remaining slots, a cap on any one player's share of the purse, and money always kept back for the minimum squad.
- Human-like thinking time (0.6–2.4 s, longer near its limit), always inside the bid timer.
- In friend rooms, a disconnected player's team is run by AI after a 60 s grace period, until they reconnect.

## 5. Squad rating (after the auction)

- Best XI picked automatically (keeper, ≥5 bowling options, ≤4 overseas).
- Scores out of 100: **Batting**, **Bowling** (bonus for spin + pace variety), **Balance**, **Bench depth** (not scored in Quick, which has no bench), **Value for money** (strength vs spend compared with the league) → grade A+ … D.
- Teams ranked 1–10. In testing, the top-rated squad wins the title ~25–30% of the time: rating predicts but doesn't decide.

## 6. Simulated season (no gameplay)

- Single round-robin (9 matches per team) → Qualifier 1, Eliminator, Qualifier 2, Final.
- Ball-by-ball from batter vs bowler skill, phase (powerplay/middle/death), pitch type (flat / balanced / spin / seam), set batters, chase pressure. Bowling plans use death specialists at the death, pace up front, spin in the middle; max 4 overs, never consecutive.
- Super Over for ties.
- Output: scorecards with dismissals, points table with NRR, Player of the Match, Top Run-Scorer, Top Wicket-Taker, MVP, Champion.
- Seeded: the same squads + seed always replay the same season (the server runs it for friend rooms).
- Tuned averages: first innings ~180, ~6 wickets per innings, ~20% of totals 200+, chasing side wins ~54%.

## 7. Player data (server-driven)

### Source and pipeline

- `data/source/players_reference.csv`: **329 players** = all 250 IPL 2026 squad players (retained + bought) + the 79 who went unsold in the Dec 2025 mini auction. Sources: Wisden squad list and unsold list, Wikipedia personnel-changes page (retention and auction prices). Cricinfo blocks automated access, so it wasn't used.
- Columns: real name / **game_name** / country / role / bowl type / hand / batting, bowling, keeping ratings / capped / base / real price / real team / status / tags / notes. Ratings are my estimates (0–100); rows marked `verify role` in notes are domestic players whose role I couldn't confirm.
- **Real names, real prices and real teams are reference only and never ship.** `tool/build_dataset.dart` exports only `game_name`, fails the build if any row has no game name or a game name identical to a real one, and a test checks the output has no reference columns.
- Base price: real auction base where the player was in the auction; otherwise derived from rating (capped ₹2 Cr…50 L; uncapped 50/40/30 L).
- `data/source/league.json`: franchises, rules, formats and rating scale. Edit this, rebuild, publish.
- **Star power** (`star` column, 0–100, set for the 56 biggest names): raises market value up to +80% so famous players spark bidding wars; has **no effect on match results**. Lifted price realism from 0.73 to **0.79** rank correlation with real prices; mega record buy ~₹25.6 Cr (max ₹28 Cr) vs real ₹27 Cr.
- **previousTeam**: from each franchise's `sourceTeam` in `league.json` (25 players per team). Drives retention and RTM. Unsold players are free agents.

### Fields

```
Player  { id, name, country, overseas, capped, role (BAT|BOWL|AR|WK),
          leftHanded, bowlType (pace|spin|none),
          batting, bowling, fielding, keeping (0–100), form (-5..5),
          basePrice (lakhs), set, tags (opener, finisher, powerplay, death),
          image? }
Franchise { id, name, shortName, city, primaryColor, secondaryColor }
Rules     { increments, basePrices, timers, maxOverseasInXI, setOrder, formats }
```

All money is whole lakhs (₹1 Cr = 100), so purses never drift.

### Player images

- `image` is optional: a key on the content CDN. Missing → a **silhouette avatar** in the franchise colours (or neutral colours before the player is sold). Expected for most uncapped players.
- Images ship with the data, not the app, so they can be added over time.
- Portraits: one consistent **illustrated** style, generated from per-player appearance tags (skin tone, hair, beard, build, headgear/glasses, kit colours) so a player keeps a familiar overall look. They are not generated from photos or by naming the real person, so the face itself is original.

### Team crests

- `tool/gen_logos.dart` draws an original crest per franchise (shared shield, team colours from `league.json`, one emblem each) → `data/logos/<ID>.svg` + `preview.html`. Re-run after changing colours. A new franchise id needs an emblem added to the tool.

## 8. Presentation: auction host, voice and SFX

**The auction hall** (main screen):

- A hall with **10 team tables**, each with its characters seated behind the team crest and name board. Your table sits at the front.
- When a team bids, a character at its table **raises the paddle** (AI and friends alike), and the current bid + leading team's crest update in the centre.
- **Player reveal**: the assistant draws a chit from the box → animation → the **player card fills the centre of the screen** (portrait, name, role, country, ratings, base price, previous team) → the card settles and bidding opens.
- Bottom bar: **Bid ₹X** (always the exact next amount), **Pass**, and **hold-to-fast-forward**; your purse, squad count and overseas count; RTM card indicator.
- RTM moment: the timer ends → the previous team's table lights up with the RTM card → "Use RTM?" for you, or the AI's answer → gavel.

**Two characters** (original and stylised, not modelled on any real auctioneer):

- **Auctioneer** at the podium: calls the base price, points at bidding paddles, "going once / going twice", bangs the gavel for **SOLD** / **UNSOLD**.
- **Assistant** beside the player box: draws the chit, reveals the player card on the screen, and moves the sold card to the winning team's board.
- One lot's sequence: assistant draws → card reveal → auctioneer announces base → bidding → gavel → assistant files the card → idle.
- Built with **Rive** (vector animation with a state machine, small files, runs smoothly in Flutter). The character art needs a Rive artist or a purchased rig; I'll wire it to the auction events.

**Voice**: a pre-generated clip library in one consistent neural-TTS voice, stitched together at runtime (much more natural than on-device TTS):

- Phrases: "Next up…", "base price", "Do I hear…?", "Going once", "Going twice", "Sold to…", "Unsold", set announcements
- Amounts: built from number pieces (e.g. "two crore" + "twenty lakh"), covering every amount the increment table can produce
- Team names (10) and **player names**: generated by the content pipeline and downloaded with the data, so new players get spoken names without an app update
- English first; Hindi/Hinglish voice pack later

**SFX**: gavel, paddle bid, card whoosh, timer tick (last 3 s), SOLD stinger, crowd murmur and applause (louder for big buys), purse counter. Only royalty-free or licensed packs.

**Settings**: voice on/off, SFX volume, and **fast mode** (short animations; lots only AI teams bid on resolve in ~2 s).

## 9. Updates without an app release

- Player data, teams, rules, images and voice clips are published as **versioned files** on AWS S3 + CloudFront.
- On launch the app checks a small `manifest.json` (data version, minimum app version, checksums) and downloads what changed.
- A default dataset ships inside the app, so first launch and offline solo play still work.
- **Can update without an app release:** names, ratings, base prices, roles, teams, rules, images, voice clips, new player pools.
- **Still needs an app release:** new screens, new features, bug fixes.
- v1: edit a spreadsheet → script converts it to JSON → publish. A web admin panel can come later.

## 10. Tech stack

| Part | Choice |
|---|---|
| App | Flutter (Android), Rive for the host, `just_audio` for voice + SFX |
| Game logic | **`packages/auction_core`**, pure Dart: rules, AI bidders, squad rating, season sim. Used by the app (solo) and the server (rooms), so both modes always behave the same. |
| Room server | Dart (`shelf` + WebSockets); the server decides every bid; rooms are held in memory |
| Content | Static files on S3 + CloudFront |
| Identity | Guest: device ID + nickname (no login in v1) |
| Hosting | One small AWS instance (Lightsail/EC2) |

At v1 scale there's no database or Redis: one server process handles every bid in order, so two bids can't conflict.

## 11. Build phases

1. ✅ **`auction_core` + tests**: rules, AI bidders, rating, season sim, plus `bin/simulate.dart` for headless tuning.
2. ✅ **Solo mode app** (`app/`, portrait): home → setup (team, Fresh/Retention, length) → retention picker → **auction hall** (10 tables with paddle-raising characters, auctioneer + assistant with the box, card reveal, timer with "going once/twice", recent sales, SOLD/UNSOLD/RTM stamp, RTM prompt, Bid/Pass/hold-to-fast-forward, pause) → squad ratings → season (table, results + scorecards, awards). Placeholder vector characters and silent `AudioCues` hooks, ready for the Rive art and voice pack. Timing lives in `LiveAuction` (core), driven by frame time, so the room server can reuse it.
3. ✅ **Content pipeline** (S3 + CloudFront CDN; the legacy pipeline scripts are no longer in this repository): spreadsheet → `build_dataset` → `publish.ps1` (hash-named files, manifest last) → S3 (private) + CloudFront. App checks `manifest.json` on launch, verifies SHA-256, caches, falls back to bundled data; `minAppBuild` gates old builds. Images and voice clips plug into the same manifest later.
4. ✅ **Friend rooms** (legacy Dart server on EC2 t3.small + Caddy; superseded by `server-cs/AplRooms`): the server runs the real `LiveAuction`; phones replay its action log on their own engine (`RemoteAuction`) and send intents (bid at the amount they saw / pass / RTM). Lobby with 6-letter codes, team picks, host start (min 2 humans), retention submissions (90 s window), AI for empty teams, AI takeover after 60 s offline and hand-back on reconnect (full log replay), same season on every phone via a shared season seed. Rooms live in memory: a server restart ends running rooms.
5. **Polish + Play Store**: Rive host, full voice pack, animations, analytics, store listing.

## 11b. 3D client (Unity) — replaces the Flutter app (decided 2026-10-20)

Feedback on the Flutter build: "very basic" — needs a real 3D hall like the reference (Real Cricket–style auction), characters at team tables, staged pacing, voice and SFX. Decisions: **full Unity app**, **free licensed assets first**, **Indian English voice**, **landscape**.

- **Engine:** Unity 6 LTS, URP, Android, landscape. `client/` (to create once Unity is installed).
- **Game logic:** `packages/AuctionCore.Net` — C# port of `auction_core`, netstandard2.1/C# 9 (Unity-compatible), consumed as a local UPM package. **Bit-identical to Dart**: 15 conformance tests replay golden files from `tool/export_golden.dart` (RNG, lot order, 12 full AI auctions incl. retention + ratings + seasons, scripted live auctions, room replay). Regenerate goldens after any rule change.
- **Unchanged:** room server (Dart, live), content CDN (live), data pipeline. Unity client speaks the same room protocol (`RemoteAuction`) and manifest.
- **Hall:** stage with LED backdrop (APL crest), auctioneer at podium, assistant beside the player box; 10 team tables, 3–4 seated characters each in team-colour kit, paddle, laptop, bottles, team flag. Characters: Microsoft Rocketbox (MIT) + Mixamo animations (sit idle, talk, raise paddle, clap, gavel).
- **Camera director (Cinemachine):** wide hall → stage on draw → card close-up → cut to the bidding team's table on each bid → winner's table on SOLD.
- **Lot pacing (target, ~20–35 s per contested lot):** assistant draws (~2 s) → card reveal + auctioneer announces name/role/base (~4–5 s) → bidding; AI thinks 1.5–4 s per raise; "going once" (~2 s) → "going twice" (~2 s) → gavel "SOLD" + winner shot (~3 s). Skip (Pass) and hold-to-fast-forward stay. Pacing values move into the server-driven rules so solo and rooms share them.
- **Audio:** Amazon Polly generative voices (us-east-1): **Arjun** (auctioneer, en-IN male) + **Kajal or Kiara** (assistant, en-IN female; auditions in `audio/auditions/`). Clip library stitched at runtime (names, amounts, phrases). CC0 SFX: gavel, paddle, crowd murmur, applause, timer ticks, SOLD stinger; music bed.
- **Pre-auction ceremony (skippable):** auctioneer intro → for each franchise (human's team last, "And finally…"): LED wall switches to its crest + colours, its three table members walk on from a wing, line up on stage marks, pose (arms crossed / fist pump / wave / victory) while the auctioneer says "From {city}… please welcome… the {team}!", applause, wide → hero camera, walk off → "Now, let the auction begin!". ~70 s. SKIP CEREMONY button throughout; setting to always skip.
- **Overlays (uGUI):** reference layout — balance, player card (bat/bowl/type/base), current bid, bidding team badge, RTM available, Bid / Pass(Skip) / Hold.
- **Crowd sound (user ask, 2026-10-20):** recorded public-domain/CC0 clips from Wikimedia Commons (`tools/fetch_sfx.sh`, credits in `client-assets/sfx/CREDITS.md`): room murmur loop that swells as a lot gets contested and hushes on "going twice", "ooh" at ₹10 Cr / ₹20 Cr jumps and on RTM, gavel, applause in three sizes by price, cheers for your signings and ₹15 Cr+ buys, cheers + applause for every team walk-on and the closing ovation.
- **Opening (user ask):** ceremony starts with the auctioneer welcoming team owners, coaches, analysts and franchise families to applause; after the walk-ons, a ~35 s **rules briefing** (skippable) — fixed purse, squad min/max and overseas cap for the chosen format, auto-fill below the minimum, retention (if any), the RTM card, "going once, going twice, sold" — with an on-screen rules card that highlights each rule as it's read, ending on the controls (BID / SKIP / hold ►►). Purse and squad lines are generated per format from the dataset.
- **Round 3 (user feedback 2026-10-21):** friend rooms live in the Unity app (RoomClient WebSocket + OnlineLobby: name, create/join by code, team pick, host start, retention; rooms skip ceremony/briefing because the server clock is already running); server pacing now matches the 3D hall (RoomConfig revealMs 11000 / resultMs 5500 / aiRtmThinkMs 6500 / aiThinkScale 3.6). Camera: bids cut to the table then back to the hall; auctioneer close-ups only for his calls ("anyone else?" at most once a lot). Assistant stands beside the podium and hands the card over without walking. FAST is a tap toggle; SKIP passes and races the AI bidding silently, then slows for "going once/twice/sold" at the final price. Gavel tap on each "going" call, double rap on SOLD (voice and applause wait for it). Side walls carry a team banner each. Hub "Remaining" grouped by set (random within a set, no order shown). RTM: chip shows cards left; pulses "RIGHT TO MATCH AVAILABLE" when the player on the block was yours; after the hammer the auctioneer asks the holder on camera; they raise a gold RTM card or shake their head (procedural).
- **Menus over the live hall:** title → pick franchise (10 crest tiles; your table is swapped to the front centre) → format (Quick / Standard / Mega) + Fresh or Retention → retention picks (top 12 of last season's squad, slab prices and purse shown live, rule problems block START) → ceremony → auction. Friend rooms button is there but disabled until the Unity room client lands.
- **Results:** squad ratings table (rank, grade, OVR/BAT/BOWL/BAL, spent), my squad (best-XI marked, retained/RTM/auto-fill tags), season (standings, champion + final margin, orange/purple cap, MVP, your verdict) → play again. Season seed = auction seed × 7919 + 13, same as the Flutter app.
- **Checks without a device:** `tools/unity-check` compiles `client-src` against an installed Unity's DLLs + uGUI source (`dotnet build tools\unity-check\UnityCheck.csproj`). `tools/unity_bootstrap.ps1 -PlayTest` builds a Windows player and runs a scripted play-through (`-aplshots <dir>`: menus, retention, ceremony, auction at 12×, every results tab) that saves screenshots and logs every exception.

## 12. Not in v1

Public matchmaking, login/accounts, global leaderboards, monetisation, chat,
retentions/RTM, trades.

## 13. Open questions

1. Review the drafted **game_name**s (all 329 filled; edit any in the sheet).
2. **Monetisation plans** (ads / in-app purchases / none): not needed for v1.
3. Character art for the hall (seated team characters, auctioneer, assistant): commission a Rive artist or buy a rig.

## 14. Tuning status (real player pool, 300 sims per format)

- Grade curve is in `league.json` (`rating`, with a Quick override). Mega: A+ 3% · A 43% · B+ 43% · B 11%. Quick: mostly A to C+.
- AI title share (aggressive / balanced / value-hunter): Mega 34/45/21, Standard 17/40/43, Quick 17/19/64. Quick still favours value-hunters.
- 0 rule violations in every run, across all 3 formats × fresh/retention.
- RTM: ~9.5 cards used per mega auction, on high-value former players (Abhishek, Kohli, Rashid, Varun at ₹21–25 Cr), not the first one to come up.
- Retention mode: every AI team keeps 3; record buy drops to ~₹17 Cr (less money in the room, like real retention years).
- **Auto-fill gives fillers, not stars** (fixed in phase 2): it used to hand out the best leftovers, so a team that never bid finished #1 with an A+. Now it gives the cheapest, lowest-rated player for a missing role; a test checks an idle team always rates below the median.
- Quick + Retention grades run high (64% A+): 3 kept stars in an 11-player squad. Option: `maxPlayers: 2` for Quick.
