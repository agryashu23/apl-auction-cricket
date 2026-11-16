# APL rooms protocol v2

The contract between the Unity client (`client-src/Assets/Apl/Runtime/Online/RoomClient.cs`) and the C# room server (`server-cs/AplRooms`).
It replaces the v1 protocol of the old Dart server (`server/`, kept untouched for reference).

v2 stays as close to v1 as is sensible:
- Every message is one JSON object with a type field `t`.
- The handshake, `create` / `join` / `pick` / `start` / `retain` / `bid` / `pass` / `rtm` and the server's `welcome` / `room` / `retention` / `start` / `act` / `clock` / `kicked` / `error` / `pong` keep their v1 shapes.
- The client still replays the auction from `RoomSetup` + the action log with `RemoteAuction` (engine `Rooms.cs`).

What is new in v2:
- The new rules.
- Host controls.
- Pass consensus.
- SIMULATE (autopilot) and take-back.
- Pause.
- The bid timer setting.
- Force close and undo.
- The end-of-auction rewards.

## 0. Running it locally

```
dotnet run --project server-cs\AplRooms                     # http://localhost:8787, ws://localhost:8787/ws
curl http://localhost:8787/health
dotnet run --project server-cs\RoomBots                     # the end-to-end harness (starts its own server on :8790, game time x10)
dotnet run --project server-cs\RoomBots -- --url ws://localhost:8787/ws   # against a running server started with
                                                            #   --time-scale 10 --takeover-sec 1.5 --retention-sec 4
```

- Unity test build: `-aplroomurl ws://127.0.0.1:8787/ws`.
- **Port: 8787**, on `localhost` by default. For a phone on the same Wi-Fi, start it with `--host 0.0.0.0` and use `ws://<pc-ip>:8787/ws` (Windows may ask to allow it through the firewall).
- Client test setup (Unity side): the server on 8787; guests read the room code from `%TEMP%\apl_room_code.txt` (`join:auto`). The server does not write that file.
- The server loads `data/apl_dataset.json` from the repo, which is the same file as the client's `Resources/Data/apl_dataset.json`. Pass `--data-file <path>` to use another.
- Server flags, each with its default:

| Flag | Default |
|---|---|
| `--port` | `8787` |
| `--host` | `localhost` (`0.0.0.0` for the LAN) |
| `--data-file` | the repo's `data/apl_dataset.json` |
| `--min-players` | `2` |
| `--takeover-sec` | `30` |
| `--retention-sec` | `150` |
| `--time-scale` | `1` (game ms per real ms; tests only) |

Local only: nothing here deploys anywhere.

## 1. Rules a room plays by

The new rules are the same as solo:
- IPL sets lot order (`LotOrder.IplSets`).
- **Franchise AI** for every team without a human.
- Reputation prices.
- The **28 Cr** price cap (`rules.maxPrice`).
- Retention.

`RoomSetup.rules = 2` tells the replica to build exactly this engine.

The server is authoritative. It checks:
- every bid: the amount must equal the next increment, plus purse, squad size, overseas limit and price cap;
- the timers;
- RTM;
- retention validity.

The client may pre-check with `Engine.CheckBid` for button states, but only the server decides.

Other limits:
- Max humans per room = number of franchises (10). Each human has a different franchise. Every franchise without a human is a Franchise-AI bot.
- **No FAST, no SKIP, no hold-to-fast-forward.** The server answers `fast`, `skip` and `ff` messages with `error notAllowedInRooms`. The client hides those buttons.
- `clock.ff` can still be `true`. That means the server is racing through a lot that no human can bid on (every human has passed or is ineligible). It is server-driven and the same for everyone.

## 2. Room lifecycle

```
lobby ──start──► retention (only if retention is on) ──all humans submitted / deadline──► auction ──last lot / end / all simulated──► finished
```

| Phase | What happens |
|---|---|
| `lobby` | Players join with the code and pick distinct franchises. The host sets the format, retention, timer and lock, kicks players and transfers host. |
| `retention` | Every human submits keeps (`retain`). Deadline: `retentionLeftMs` (150 s). A human who didn't submit gets the AI's picks for his team. Bots always take the Franchise AI's picks. |
| `auction` | The server runs `LiveAuction`. Phones replay `act`s and show `clock`. |
| `finished` | The final squads + `results` (rewards) are in the `room` snapshot and in a `results` message. The room is kept 30 min. |

- **Format:** `mega` (fresh mega auction) or `standard` (the mini auction; alias `mini`). `standard` is retention-only, so retention is always on for it.
- **Start conditions:** host only, at least `minPlayers` seated humans, every member seated, and every member other than the host **ready** (the host's START is his ready). Errors in that order: `notHost`, `notEnoughPlayers`, `unseatedPlayers`, `notReady`.

## 3. Client → server messages

All messages need `hello` first (else `error helloFirst`).

### Connection

| `t` | Fields | Notes |
|---|---|---|
| `hello` | `protocol: 2`, `playerId` (stable per install, 1–64 chars), `name` (≤20), `dataVersion` | → `welcome`, or `error protocol {need:2}` / `badPlayerId`. |
| `ping` | | → `pong`. |
| `create` | `format`, `retention` (bool), `team` (the host's franchise), optional `timer` (sec) | Creates a room; the creator is host. → `room`. |
| `join` | `code` | Also how you **reconnect**: same `playerId` → same seat. |
| `leave` | | Lobby: the seat is freed. Auction: your team goes on autopilot (`away`) at once; rejoin to take it back. |

`create` and `join` check `dataVersion` against the room's (`error dataVersion {need}`).

### Lobby

| `t` | Fields | Who |
|---|---|---|
| `pick` | `team` | anyone (lobby). Errors: `teamTaken`, `unknownTeam`, `started`. |
| `ready` | `ready` (bool, default true) | anyone (lobby). Shown as `members[].ready`. Error: `started`. |
| `start` | | host. Errors: `notHost`, `notEnoughPlayers`, `unseatedPlayers`. |
| `retain` | `ids: [playerId…]` | seated human (retention phase). Errors: `invalidRetention {detail}`, `unknownPlayer`, `notRetention`. |

### Auction (the seat's own team)

| `t` | Fields | Notes |
|---|---|---|
| `bid` | `amount` (lakhs) | Must equal `Engine.NextBidAmount`. Errors are the `BidRejection` names in camelCase: `staleAmount`, `cannotAfford`, `squadFull`, `overseasLimit`, `priceCap`, `alreadyLeading`, `passed`, `biddingClosed`, `noOpenLot`. Also `paused`, `notInControl` (your team is on autopilot). |
| `pass` | | Out of **this lot only**. See §6. Errors: `biddingClosed`, `alreadyPassed`, `leading` (the high bidder can't pass), `paused`, `notInControl`. |
| `rtm` | `use` (bool) | Answer your RTM offer. Error: `noRtm`. |
| `simulate` | | Hand your team to the Franchise-AI autopilot (§7). Error: `notInControl`. |
| `takeBack` | | Take your team back from the autopilot (`autopilot` or `away`). Error: `notOnAutopilot`. |
| `fast`, `skip`, `ff` | | Always `error notAllowedInRooms`. |

### Host only

Any other sender gets `error notHost`.

| `t` | Fields | Phases | Effect |
|---|---|---|---|
| `settings` | any of `format`, `retention`, `timer` | lobby (`timer` also in auction) | `timer` is 5–30 s (offer 10 / 15 / 20). In the auction it applies from the next bid. Errors: `unknownFormat`, `badTimer`, `started`. |
| `lock` | `locked` (bool) | any | A locked room refuses new players (`error locked`); members can still reconnect. |
| `kick` | `player` | any | Removes the player; he gets `kicked` and can't come back. In an auction his team becomes a Franchise-AI bot (`control: "ai"`). |
| `host` | `player` | any | Transfers host to another connected member. Error: `notInRoom`. |
| `pause` / `resume` | | auction | Freezes every timer: the bid timer, the reveal / result / RTM phases and AI thinking. While paused, bids and passes get `error paused`. The host can still force close, undo or end. |
| `forceClose` | `sold` (bool) | auction, a lot open | `sold: true`: bidding ends now and the lot goes to the current high bid (an RTM offer still follows if the rules give one; in the RTM phase it closes with RTM declined). With no bid it goes unsold. `sold: false`: unsold, whatever the bids. Error: `noOpenLot`. |
| `undo` | | auction | Undoes the last closed lot (sold or unsold): squads, purses and RTM cards go back, and that player comes up again. The server resends `start` with the shortened log (§4). Error: `nothingToUndo`. |
| `end` | | auction | Ends early: every human team goes on autopilot and the server finishes the rest instantly (same as everyone simulating). |

- Host passes automatically to the next connected human (join order) when the host disconnects, leaves or is gone.
- The old host does not get it back on reconnect.

## 4. Server → client messages

| `t` | Shape | When |
|---|---|---|
| `welcome` | `{protocol: 2, dataVersion, server: "apl-rooms-cs"}` | After `hello`. |
| `room` | `{room: Room}` (§5) | On any lobby / seat / host / phase / setting change, and on join. |
| `retention` | `{leftMs, submitted: [teamId…]}` | Retention phase: on entry, on every submit, on join. |
| `start` | `{setup: RoomSetup, log: [action…], reason?}` | Auction start, on (re)join, and again whenever the log is rewritten: `reason: "undo"` or `"instant"` (all-simulate / end finished everything in one go). **On every `start`, rebuild with `RemoteAuction.Load(setup, log)`** and redraw from the engine state. Don't animate the gap. After `start {reason:"undo"}` the log stops just before the undone lot, and an ordinary `act {a:"open"}` for that lot follows. |
| `act` | `{a: action}` | Each engine step, in order (v1 shapes, below). |
| `clock` | §4.1 | Every 250 ms in the auction, and after each `act`. |
| `notice` | `{kind, by?, player?, team?, …}` | One-off events for toasts (§4.2). The state itself is always in `room` / `clock`. |
| `results` | `{results: Results}` (§8) | When the auction finishes, and on join to a finished room. |
| `kicked` | `{code}` | You were removed by the host. Don't auto-rejoin. |
| `error` | `{code, for?, detail?, need?}` | `for` = the `t` of the message that failed. |
| `pong` | | |

**Actions** (`RoomAction`, replayed by `RoomReplay.Apply`; identical to v1 except the `unsold` flag):

```json
{"a":"open","p":"<playerId>"}         // next lot; "p": null = auction finished
{"a":"bid","team":"MBH","amount":200}
{"a":"pass","team":"CHC"}
{"a":"end"}                            // bidding over, RTM offer pending
{"a":"close","rtm":false}              // lot closed (sold to the high bid / unsold if no bid)
{"a":"close","rtm":false,"unsold":true}  // NEW: host forced unsold; the replica drops the bids first
```

`RoomSetup` (`RoomSetup.ToJson()`):

```json
{"seed":123,"seasonSeed":456,"format":"mega","retention":true,"retentions":{"MBH":["p1","p2"],"CHC":[]},"dataVersion":7,"rules":2}
```

### 4.1 `clock`

```json
{"t":"clock","phase":"bidding","lot":12,"bid":7350,"bidTotal":10000,"left":0,"rtmTotal":10000,
 "ff":false,"paused":false,"humans":["MBH","KOC"],"passed":["KOC"],"timerSec":10}
```

| Field | Meaning |
|---|---|
| `phase` | `revealing`, `bidding`, `rtmDecision`, `result` or `finished` |
| `bid` | Bid-timer ms left (bidding only) |
| `bidTotal` | Full bid timer, ms (accelerated round: shorter) |
| `left` | Ms left in the reveal, RTM or result phase |
| `rtmTotal` | The human RTM answer window, ms |
| `ff` | Server racing through a lot no human can act on |
| `paused` | Host paused: freeze every countdown |
| `humans` | Teams a human is driving right now (not autopilot or AI) |
| `passed` | Teams that passed on this lot |
| `lot` | Lot number (0 between lots / before the first) |
| `timerSec` | The host's timer setting |

Every `clock` carries every field above (`passed`, `lot`, `paused`, `timerSec` included). `RemoteAuction.Clock` reads all of these. The new fields are optional, so a v1 clock still parses. `RemoteAuction.Paused` / `Passed` / `LotNumber` / `TimerSec` expose them, and `Advance` does nothing while paused.

### 4.2 `notice` kinds

| `kind` | Fields |
|---|---|
| `paused`, `resumed` | `by` |
| `timer` | `by`, `timerSec` |
| `hostChanged` | `player` (new host), `reason`: `transfer` / `disconnect` / `leave` |
| `kicked` | `player`, `team` |
| `locked` | `locked` |
| `simulate` | `player`, `team` |
| `away` | `player`, `team` (grace period over, bot covering) |
| `takeBack` | `player`, `team` |
| `forceClose` | `by`, `sold` |
| `undo` | `by`, `lot` (the lot re-auctioned) |
| `ended` | `by` |
| `instantFinish` | The server finished the rest because every human was on autopilot |

## 5. The `room` snapshot

```json
{
  "code": "K7QX2M", "host": "<playerId>", "phase": "lobby|retention|auction|finished",
  "format": "mega", "retention": false, "timerSec": 10, "locked": false, "paused": false,
  "dataVersion": 7, "protocol": 2, "rules": 2, "minPlayers": 2, "maxPlayers": 10,
  "members": [
    {"id": "<playerId>", "name": "Asha", "team": "MBH", "connected": true, "host": true, "ready": true, "control": "human"}
  ],
  "teams": [
    {"team": "MBH", "owner": "<playerId>", "name": "Asha", "control": "human"},
    {"team": "CHC", "owner": null, "name": null, "control": "ai"}
  ],
  "results": null
}
```

`control` is one of:

| Value | Meaning |
|---|---|
| `human` | The player is driving (also every member in the lobby) |
| `autopilot` | He pressed SIMULATE |
| `away` | Disconnected past the grace period (or left mid-auction): autopilot until he rejoins |
| `ai` | A franchise no human holds, or the human was kicked mid-auction: a full Franchise-AI bot |

- `members[].ready`: lobby READY (always true for the host).
- A kicked player is removed from `members`; his team's `control` becomes `ai`.
- `results` is filled once `phase == "finished"`.

## 6. PASS semantics

- `pass` puts **your team** out of the bidding for **this lot only**. It is recorded on the lot (`act pass`, and `clock.passed`). You can't bid on that lot any more.
- Your next lot is fresh.
- Another player's pass never ends the lot for you.
- The lot closes **early** only when every human-driven team is settled and no bot wants to bid on. A team is settled when it:
  - has passed, or
  - is the current high bidder, or
  - cannot bid (purse, squad full, overseas, price cap).
- If bots still want the player, they keep bidding in real time. After every bot bid the check runs again. Note that the outbid human is no longer settled, so he gets the full timer to answer.
- Otherwise the lot closes when the bid timer runs out (the timer restarts on every bid).
- A pass during the reveal counts once bidding opens.
- Autopilot and AI teams are bots for this rule.

## 7. SIMULATE, away and take-back

- **`simulate`:** your team gets the **Franchise-AI autopilot**, like solo SIMULATE's AutoPilot. It bids only to fill role gaps and reach the minimum squad, within your purse, and it answers your RTM offers. The auction carries on in real time for everyone else. `takeBack` returns control while the auction is running. Seat `control: "autopilot"`.
- **Disconnect:** your seat waits `takeover-sec` (30 s). After that the autopilot covers (`control: "away"`, notice `away`). When you rejoin (`hello` + `join` with the same `playerId`), control comes back to you automatically. `leave` mid-auction is `away` at once.
- **All humans on autopilot or kicked** (none `human` or `away`): the server finishes the rest at once. The bots and autopilots settle every lot, then `start {reason:"instant"}` arrives with the full log, then `results`. Host `end` does the same.

## 8. Rewards (`results`)

Computed by the engine: `AuctionAwards.Compute(engine, data.Rules).ToJson(owner)`, pure C#, the same rules object the client has, so a client computing it locally from its replica gets identical numbers (only `owner` / `name` / `control` / `topHuman` need the room). Solo can show it too. It is deterministic: the same final squads always give the same numbers. All money is in lakhs.

```json
{
  "version": 2, "winner": "MBH", "topHuman": "MBH",
  "standings": [
    {"rank": 1, "team": "MBH", "owner": "<playerId>|null", "name": "Asha|null", "control": "human|autopilot|away|ai",
     "total": 612, "overall": 81, "grade": "A",
     "points": {"squad": 324, "balance": 90, "value": 100, "steals": 30, "starBuy": 18, "purse": 25, "roles": 25, "overseas": 0},
     "stats": {"spent": 11650, "purseLeft": 350, "purse": 12000, "squadSize": 22, "overseas": 8, "maxOverseas": 8,
               "ratingPerCr": 0.70, "steals": 2,
               "biggestBuy": {"player": "p17", "name": "…", "price": 2400} }}
  ],
  "awards": [
    {"id": "bestSquad",   "title": "Best Squad",            "team": "MBH", "detail": "Rated 81 (A)"},
    {"id": "steal",       "title": "Steal of the Auction",  "team": "KOC", "player": "p88", "price": 120, "worth": 640, "detail": "…"},
    {"id": "bigSpender",  "title": "Big Spender",           "team": "CHC", "detail": "Spent ₹118.5 Cr"},
    {"id": "biggestBuy",  "title": "Biggest Buy",           "team": "CHC", "player": "p3", "price": 2800},
    {"id": "mostBalanced","title": "Most Balanced",         "team": "RJR", "detail": "Balance 100, roles 25/25"},
    {"id": "valueKing",   "title": "Value King",            "team": "GJG", "detail": "0.93 rating points per crore"}
  ],
  "scoring": ["squad = squad rating (0-100) x 4", "…one line per rule…"]
}
```

### Scoring rules

These are the exact rules, also sent as `scoring`. `worth` is the reputation price (`Market.ReputationPrice`). Only auction and RTM buys count as "buys"; retained and auto-filled players do not.

| Category | Rule | Max |
|---|---|---|
| `squad` | Squad rating overall (`Ratings.Rate`, the results screen's number) × 4 | 400 |
| `balance` | The rating's balance score (keepers, bowling options, batters, left-hander, size) | 100 |
| `value` | Value for money = overall ÷ crores spent on the whole squad, scaled so the room's best = 150 | 150 |
| `steals` | 15 per steal | 90 |
| `starBuy` | The highest-priced buy's impact − 60, clamped 0–30 | 30 |
| `purse` | Purse used: 25 × min(1, spent ÷ 90 % of the purse) | 25 |
| `roles` | 25 − 5 per player short of the role targets (keepers, batters, all-rounders, bowlers; `Market.SquadTargets`), min 0 | 25 |
| `overseas` | 15 × min(1, overseas in squad ÷ 4) | 15 |

- A **steal** is a buy with worth ≥ 1 Cr and price ≤ 60 % of worth.
- **Total** = the sum of the categories.
- **Rank:** total, then overall, then team id. Rank 1 is the **winner**. `topHuman` is the best-ranked team that had a human owner at the end.

### Awards

Each award has one team. Ties go to the better rank.

| Award | Rule |
|---|---|
| **Best Squad** | Highest overall |
| **Steal of the Auction** | The single buy with the largest worth − price among steals (absent if no steal) |
| **Big Spender** | Most spent on buys |
| **Biggest Buy** | Highest single price |
| **Most Balanced** | Highest balance + roles |
| **Value King** | Highest rating per crore |

The leaderboard and rewards are **per room**: nothing is stored across rooms.

## 9. Robustness

- **Reconnect:** `RoomClient` already rejoins with `join {code}` after a drop. The server keeps the seat (lobby: 2 min; auction: forever, autopilot after the grace period) and resends `room`, `retention` / `start` + `clock`, and `results` if finished.
- **Room codes:** 6 characters from `ABCDEFGHJKLMNPQRSTUVWXYZ23456789`. `join` upper-cases and trims.
- **Limits:**
  - 8 KB per message (`tooLarge`).
  - 30 messages a second (`slowDown`).
  - Bad JSON (`badJson`).
  - Unknown `t` (`unknownMessage`).
  - Max rooms 500 (`serverFull`).
  - Max humans 10 (`full`).
  - A started room refuses newcomers (`started`).
  - Locked rooms refuse newcomers (`locked`).
  - Kicked players are refused (`kicked`).
- **Idle cleanup** (every minute):
  - Finished rooms are dropped 30 min after finishing.
  - Rooms with nobody connected are dropped after 10 min.
  - Lobby seats of players gone for 2 min are freed.

## 10. Error codes (for `RoomClient.Describe`)

| Area | Codes |
|---|---|
| Connection | `helloFirst`, `protocol`, `badPlayerId`, `dataVersion`, `tooLarge`, `slowDown`, `badJson`, `badRequest`, `unknownMessage` |
| Joining | `serverFull`, `noSuchRoom`, `full`, `started`, `locked`, `kicked` |
| Lobby | `notInRoom`, `unknownTeam`, `teamTaken`, `unknownFormat`, `badTimer`, `notHost`, `notEnoughPlayers`, `unseatedPlayers`, `notReady` |
| Retention | `notRetention`, `unknownPlayer`, `invalidRetention` |
| Auction | `notAuction`, `paused`, `notInControl`, `notOnAutopilot`, `alreadyPassed`, `leading`, `noRtm`, `noOpenLot`, `nothingToUndo`, `notAllowedInRooms` |
| Bids | `staleAmount`, `cannotAfford`, `squadFull`, `overseasLimit`, `priceCap`, `alreadyLeading`, `passed`, `biddingClosed`, `unknownTeam` |
