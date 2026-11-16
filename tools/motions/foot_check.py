"""Footwork and loft proof from the batting capture log (-aplbatcap): every ball's chosen foot and shot type against the
clip that was swung (focus batch 1, BAT-5 / BAT-7).

usage: python foot_check.py <autoshots/batcap/batcap.txt> [more logs...]

A BACK input must swing a back-foot clip (cut, pull, hook, punch, back-foot defence / glance), FRONT a front-foot clip,
AUTO either (it follows the length). LOFT must swing a lofted clip, STROKE / PUSH a ground one. The edge / miss clips
(78-81: the bat beaten or nicked) are reported but not counted against either rule.
"""
import re
import sys

TAP = re.compile(r"b(\d+) tap t=\S+ (\w+) aim (\w+) \((-?\d+)°\) foot (\w+) -> clip (\S+) \[([^\]]*)\]")
EDGE = ("78_", "79_", "80_", "81_")


def main():
    rows, bad = [], []
    for log in sys.argv[1:]:
        for line in open(log, encoding="utf-8", errors="ignore"):
            m = TAP.search(line)
            if not m:
                continue
            ball, typ, aim, deg, foot, clip, tag = m.groups()
            edge = clip.startswith(EDGE)
            is_back = tag.startswith("back-foot")
            lofted = "lofted" in tag
            ok_foot = edge or foot == "auto" or (foot == "back") == is_back
            ok_loft = edge or (typ == "Loft") == lofted
            rows.append((ball, typ, aim, foot, clip, tag, ok_foot, ok_loft))
            if not (ok_foot and ok_loft):
                bad.append(rows[-1])
    print(f"{'ball':>4} {'type':<7} {'aim':<11} {'foot':<5} {'clip':<30} {'kind':<22} foot  loft")
    for r in rows:
        print(f"{r[0]:>4} {r[1]:<7} {r[2]:<11} {r[3]:<5} {r[4]:<30} {r[5]:<22} {'ok' if r[6] else 'WRONG':<5} {'ok' if r[7] else 'WRONG'}")
    n_foot = sum(1 for r in rows if r[3] != "auto")
    print(f"\n{len(rows)} balls ({n_foot} with the foot chosen): foot wrong on {sum(1 for r in rows if not r[6])}, "
          f"loft wrong on {sum(1 for r in rows if not r[7])}")


if __name__ == "__main__":
    main()
