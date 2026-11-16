# Auction rating loop (owner, 2026-11-16)

The goal: the AUCTION game scores 8.5/10 or more overall. The loop runs:

1. rater rates;
2. developers fix the ranked issues;
3. the PM builds and captures fresh evidence in Unity;
4. the same rater rates again;
5. repeat until the score is 8.5 or more.

## Scope

The scope is the auction game only. Match gameplay (batting, bowling, fielding) is OUT of scope, except where the auction hands squads to the tournament.

## Categories

Each category is scored 0–10:

1. **Solo auction flow:** setup → retention → auction → results, and the pacing.
2. **Auction realism and rules:** IPL sets order, prices, AI bidding behaviour, retention/RTM, purse/squad/overseas rules, accelerated round.
3. **AI franchises:** sensible squads, no passive or broken teams, believable bidding wars.
4. **Auction UI/HUD:** clarity, readability, layout on phone sizes, no overlaps, the auction hub (sets, squads, teams).
5. **Presentation:** hall, auctioneer, camera, props, audio and voice lines, ceremony.
6. **Multiplayer rooms (lobby):** create/join, team picker, ready, host controls in the lobby.
7. **Multiplayer rooms (auction):** host panel, pass consensus, SIMULATE bot takeover and take-back, reconnect, toasts, no FAST/SKIP.
8. **Results and rewards:** podium, standings, points breakdown, awards, career totals.
9. **Tournament after auction:** squads carried over exactly, XI selection, tournament hub/table flow.
10. **Home screen and navigation:** auction-first, discoverability of modes, back paths.
11. **Robustness:** errors, edge cases, timeouts, rejoin, log errors/exceptions.

## Rules for the rater

- **Rate only what exists.** Don't deduct for features nobody asked for, except basics a good auction game needs.
- **Back every issue with evidence:** a screenshot file, a log line or file:line.
- **Rank issues by severity:** HIGH / MED / LOW, each with a suggested fix and the files involved.
- **Give the overall score** as a weighted judgement, not a plain average. Also say what would raise each category under 8.5.
- **Write to** `build\review\ratings\auction-rN.md`. The rater never edits game files and never runs Unity.

## Evidence

The PM captures it into `build\review\auction_rN\`:

- `solo\`: AutoShot play-through, `-PlayTest`.
- `rooms\`: 3-window `-aplroomtest`, plus logs and server.log.
- `solo_tourney\`: `-aplsimulate -apltourneyafter`. This covers the tournament screens and sim buttons. No match or gameplay captures: the owner wants auction only.
