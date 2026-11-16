"""Shot direction proof from the batting capture log (-aplbatcap): for every ball played, the clip's own direction
(batting.json angle_deg, 0 = past the bowler, clockwise from above, 0-180 off side) against the ball's exit angle
(engine: signed, + off side), and the bat face the rig turned to.

usage: python shot_direction.py <autoshots/batcap/batcap.txt> [more logs...]

Prints one row per ball and the summary: mean / max |clip - exit|, and how many within 15, 25 and 35 degrees.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DATA = os.path.join(HERE, "..", "..", "client-src", "Assets", "Resources", "Data", "cricket", "batting.json")
TAP = re.compile(r"b(\d+) tap t=\S+ (\w+) aim (\w+) \((-?\d+)°\).*?-> clip (\S+?);? exit (-?\d+)° ([\d.]+) m/s (\w+).*?face (-?\d+)°")


def signed(a):
    a = a % 360
    return a - 360 if a > 180 else a


def main():
    clips = {os.path.splitext(c["file"])[0]: c for c in json.load(open(DATA))["clips"] if "angle_deg" in c}
    rows = []
    for log in sys.argv[1:]:
        hand = {}
        for line in open(log, encoding="utf-8", errors="ignore"):
            m = re.match(r"--- ball (\d+): .*? (RH|LH),", line)
            if m:
                hand[int(m.group(1))] = m.group(2)
            m = TAP.search(line)
            if not m:
                continue
            ball, typ, aim, aim_deg, clip, exit_deg, speed, grade, face = m.groups()
            name = clip[4:] if clip.startswith("lhb_") else clip
            c = clips.get(name)
            if c is None:
                continue
            # the clip's angle is the right-hander's; the engine's exit angle is already per batter (off side +)
            ca = signed(c["angle_deg"])
            rows.append((os.path.basename(os.path.dirname(log)), int(ball), hand.get(int(ball), "?"), typ, aim, int(aim_deg),
                         name, ca, int(exit_deg), int(face), grade, abs(signed(ca - int(exit_deg)))))
    print(f"{'run':<10}{'ball':>4} {'hand':<4} {'type':<7} {'aim':<11} {'clip':<28} {'clip°':>6} {'exit°':>6} {'face°':>6} {'grade':<8} {'|d|':>4}")
    for r in rows:
        print(f"{r[0]:<10}{r[1]:>4} {r[2]:<4} {r[3]:<7} {r[4]:<11} {r[6]:<28} {r[7]:>6} {r[8]:>6} {r[9]:>6} {r[10]:<8} {r[11]:>4}")
    if not rows:
        print("no balls played")
        return
    d = [r[11] for r in rows]
    print(f"\n{len(rows)} balls: mean |clip-exit| {sum(d) / len(d):.1f} deg, max {max(d)} deg; "
          f"within 15: {sum(x <= 15 for x in d)}, 25: {sum(x <= 25 for x in d)}, 35: {sum(x <= 35 for x in d)}; "
          f"face = exit on {sum(r[8] == r[9] for r in rows)}")


if __name__ == "__main__":
    main()
