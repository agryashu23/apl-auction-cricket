# Motion pipeline

The cricket clips under `client-assets/cricket/` are **generated**, not captured: Blender
Python scripts pose the Mixamo skeleton (from `client-assets/mixamo/sit_talk.fbx`, 65
`mixamorig:` bones) frame by frame with key poses + a small IK solver, then export FBX.
`lib/` is the pack author's library (every pack shipped its `source/`), ported to run here.

- Blender: portable 4.2 LTS (matches the packs); `motion.ps1` looks in `%USERPROFILE%\tools\blender-4.2.9-windows-x64\blender.exe`, or set `APL_BLENDER`.
- Output: `build\motions\<family>/` (repo-relative) (FBX + sidecar JSON); scratch in `build\motions\work`.
- `motion.ps1 gen <script> [args]` — run a `lib/` or `drivers/` script headless.
- `motion.ps1 preview <clip.fbx> [--frames 1,31 --follow]` — stick-figure contact sheet (side / front / top).
- `reference/` — the packs' own preview PNGs and JSON (the style and metadata to match).

## Conventions (the packs', keep them)

- 30 fps, every frame keyed, exported frames `0..N-1` plus a T-pose rest key at `-1`; **metadata counts
  frames from 1** at the first exported frame (`contact_frame`, `release_frame`, `event_frame`).
- Authoring is in world metres, Z up. `mixamo_motions.py`: character faces **−Y**.
  - Batting (`batting.py`, `shots*.py`, `running_bat.py`, `reactions.py`): batter's chest faces −Y (his off
    side), **bowler at +X**, leg side +Y, keeper −X; origin between the feet on the popping crease.
    Left-handers = `running_bat.mirror_y(pose)` on the right-handed keys, files prefixed `lhb_`.
  - Bowling (`bowling.py`, `bowling2.py`, `bowler_after.py`): bowler runs toward −Y; his popping crease at
    y=0, his stumps at y=+1.22, the batter's stumps at y=−18.9; x = bowler's left. `ra_` right-arm, `la_` mirror.
  - Fielding / keeper / sequences / celebrations: start at the origin facing −Y (toward the ball / batter);
    left and right are the fielder's own.
- The bat drives the batting poses: both hands are locked to the handle every frame (`batting.BatRig`);
  `GRIP_R = 0.17` (bottom hand below the handle top), `SWEET = 0.66`, `BAT_LEN = 0.86`.
- Loops end in `_loop` and must start and end in the same pose; travelling clips carry their travel in the
  hips (Unity bakes root motion into the pose, so the body moves and the object stays).

## How a clip reaches the game

1. FBX → `client-assets/cricket/<pack dir>/` (`batting`, `batting_lh`, `bowling`, `bowling_fast`, `fielding`,
   `fielding_seq`, `keeper`, `running`, `umpire`, `after_ball`, `celebrations`, `reactions`).
   The **file name is the animator state name** (`Editor/AnimatorBuilder.BuildCricket`), so follow each
   family's naming: `NN_slug` / `lhb_NN_slug`, `ra_<action>_<over|around>_<variant>`, `catch_NN_`,
   `field_NN_`, `throw_NN_`, `run_NN_`, `seq_NN_`, `wk_`, `umpire_`, `rhb_/lhb_`, `bowler_`, `fielder_`.
2. Metadata entry → `client-src/Assets/Resources/Data/cricket/<pack>.json`, same fields as that file's other
   entries (`Runtime/Match/CricketClips.cs` reads them; batting needs `angle_deg`, `contact_frame`, `lofted`,
   `family`, `sweet_spot_at_contact_m`; bowling `release_frame`, `start_position_m`; fielding `event_frame`).
3. `tools\unity_bootstrap.ps1 -SkipCapture -PlayTest -MatchTest` imports it and plays a test match
   (screenshots in `build\unity\autoshots`).

## Agents

`~/.claude/agents/apl-motion-maker.md` writes a driver, generates, previews and hands over;
`~/.claude/agents/apl-motion-reviewer.md` judges the sheet against the brief and the reference packs.
