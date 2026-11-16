"""Round-8 match sounds: cuts, layers and normalises the CC0 downloads in
client-assets/sfx/downloads_r8 (plus the CC-BY willow knock already in Resources)
into client-src/Assets/Resources/Sfx/match/*.ogg and Resources/Music/*.ogg.

    python -I tools/build_match_sfx.py

Run with -I (the downloads are untrusted data); numpy comes from the user site.
Every output is listed with its sources in client-assets/sfx/CREDITS.md.
"""
import pathlib, site, subprocess, sys

sys.path.append(site.getusersitepackages())
import numpy as np  # noqa: E402

REPO = pathlib.Path(__file__).resolve().parent.parent
DL = REPO / "client-assets" / "sfx" / "downloads_r8"
SFX = REPO / "client-src" / "Assets" / "Resources" / "Sfx"
OUT = SFX / "match"
MUSIC = REPO / "client-src" / "Assets" / "Resources" / "Music"
R = 44100

WOOD = DL / "x_wood_and_metal_vol.2_0" / "wood_and_metal_vol.2"
BFH = DL / "x_sfx_breaking_and_falling"
QI = DL / "x_qubodupImpact" / "qubodupImpact"
QP = DL / "x_qubodupPunch" / "qubodupPunch"
C100 = DL / "x_100-CC0-SFX_0"
LJUD = DL / "x_independent_nu_ljudbank-wood_crack_hit_destruction" / "wood_impact"
KNOCK = SFX / "bat_hit.ogg"  # CC BY 4.0 BudgetPixel willow knock


def load(path, af=""):
    """Mono float32 at 44.1 kHz, through an optional ffmpeg filter chain."""
    cmd = ["ffmpeg", "-v", "error", "-i", str(path), "-ac", "1", "-ar", str(R)]
    if af:
        cmd += ["-af", af]
    cmd += ["-f", "f32le", "-"]
    return np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, dtype=np.float32).copy()


def onset(x, cut=0.08, pre=0.004):
    """Trim the lead-in so the hit lands on the clip's first frames."""
    peak = np.abs(x).max() or 1
    i = int(np.argmax(np.abs(x) > peak * cut))
    return x[max(0, i - int(pre * R)):]


def mix(*parts):
    """(signal, gain, delay seconds) layered and summed."""
    n = max(len(s) + int(d * R) for s, _, d in parts)
    y = np.zeros(n, np.float32)
    for s, g, d in parts:
        o = int(d * R)
        y[o:o + len(s)] += s * g
    return y


def finish(x, seconds, peak=0.89, fade=0.03):
    x = x[:int(seconds * R)].copy()
    f = min(len(x), int(fade * R))
    x[-f:] *= np.linspace(1, 0, f, dtype=np.float32)
    return x * (peak / (np.abs(x).max() or 1))


def save(x, path, q=5):
    path.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(R), "-ac", "1", "-i", "-",
                    "-c:a", "libvorbis", "-q:a", str(q), str(path)], input=x.astype(np.float32).tobytes(), check=True)
    print(f"{path.relative_to(REPO)}  {len(x) / R:.2f}s")


def w(name, af=""):
    return onset(load(WOOD / f"{name}.aif", af))


def knock(af=""):
    return onset(load(KNOCK, af))


def bats():
    # Middled: the deep willow knock with a dense wooden body under it.
    save(finish(mix((knock("asetrate=44100*0.93,aresample=44100,lowpass=6000"), 1, 0), (w("wood16", "lowpass=5000"), 0.55, 0.002)), 0.5), OUT / "bat_middle_2.ogg")
    save(finish(mix((knock("asetrate=44100*0.88,aresample=44100,bass=g=5:f=140"), 1, 0), (w("wood2", "asetrate=44100*0.92,aresample=44100"), 0.5, 0.001)), 0.55), OUT / "bat_middle_3.ogg")
    # Clean: a crisp knock, three woods.
    for i, (src, k) in enumerate([("wood7", 0.45), ("wood14", 0.5), ("wood3", 0.4)], start=2):
        save(finish(mix((w(src, "highpass=120"), 1, 0), (knock("lowpass=7000"), k, 0)), 0.42), OUT / f"bat_hit_{i}.ogg")
    # Power (lofted, big hits): louder crack and a longer ring of the blade.
    save(finish(mix((knock("bass=g=4:f=150"), 1, 0), (w("wood8"), 0.8, 0.001), (w("wood13", "highpass=900"), 0.35, 0.0)), 0.6), OUT / "bat_power_1.ogg")
    save(finish(mix((knock("asetrate=44100*0.96,aresample=44100"), 1, 0), (w("wood4"), 0.7, 0.0),
                    (onset(load(BFH / "bfh1_wood_hit_03.ogg", "highpass=600")), 0.4, 0.002)), 0.6), OUT / "bat_power_2.ogg")
    # Edges: thin, short, bright nicks.
    save(finish(w("wood15", "highpass=1400,asetrate=44100*1.15,aresample=44100"), 0.16), OUT / "bat_edge_2.ogg")
    save(finish(w("wood10", "highpass=1100,asetrate=44100*1.25,aresample=44100"), 0.14), OUT / "bat_edge_3.ogg")
    # Blocks: a soft dead-bat pat (defence, a push).
    save(finish(w("wood12", "lowpass=2200"), 0.25), OUT / "bat_block_1.ogg")
    save(finish(onset(load(BFH / "bfh1_wood_hit_01.ogg", "lowpass=2000")), 0.22), OUT / "bat_block_2.ogg")


def field():
    # Ball on pad: a muffled thud.
    save(finish(onset(load(QI / "qubodupImpactMeat02.flac", "lowpass=1700")), 0.3), OUT / "pad_1.ogg")
    save(finish(onset(load(QP / "qubodupPunch02.flac", "lowpass=1400")), 0.3), OUT / "pad_2.ogg")
    # Into the keeper's gloves: a leather pop.
    save(finish(onset(load(QP / "qubodupPunch04.flac", "highpass=180")), 0.28), OUT / "glove_1.ogg")
    save(finish(onset(load(QP / "qubodupPunch05.flac", "highpass=180")), 0.28), OUT / "glove_2.ogg")
    # Off the pitch: a short earthy thock.
    save(finish(onset(load(C100 / "hit_03.ogg", "lowpass=1300")), 0.2), OUT / "bounce_1.ogg")
    save(finish(onset(load(BFH / "bfh1_wood_hit_02.ogg", "lowpass=900")), 0.18), OUT / "bounce_2.ogg")
    # Into the boundary cushion: a deep padded thump.
    save(finish(onset(load(QP / "qubodupPunch02.flac", "lowpass=650")), 0.35), OUT / "rope_1.ogg")
    save(finish(onset(load(QI / "qubodupImpactMeat01.flac", "lowpass=600")), 0.35), OUT / "rope_2.ogg")
    # Stumps: ash struck hard, the neighbours knocking; bails: light wood rattling down.
    save(finish(mix((w("wood5"), 1, 0), (onset(load(BFH / "bfh1_wood_hit_03.ogg")), 0.6, 0.05)), 0.6), OUT / "stumps_1.ogg")
    save(finish(onset(load(LJUD / "impactwood14.mp3.flac")), 0.6, fade=0.15), OUT / "stumps_2.ogg")
    save(finish(onset(load(BFH / "bfh1_wood_falling_02.ogg", "highpass=500")), 0.45, peak=0.7), OUT / "bails_1.ogg")
    save(finish(onset(load(BFH / "bfh1_wood_falling_01.ogg", "highpass=500")), 0.6, peak=0.7, fade=0.15), OUT / "bails_2.ogg")


def beds():
    # Stadium bed: a thousand people talking (CC0), 40 s with soft loop edges, kept stereo-free (mono).
    x = load(DL / "crowd_eguobyte.wav", "highpass=90,lowpass=7000")[int(5 * R):int(45 * R)]
    f = int(1.5 * R)
    x[:f] *= np.linspace(0, 1, f, dtype=np.float32)
    x[-f:] *= np.linspace(1, 0, f, dtype=np.float32)
    save(x * (0.6 / (np.abs(x).max() or 1)), OUT / "crowd_bed.ogg", q=3)
    # The roar layer: the sustained middle of the big applause, looped under the bed on big moments.
    y = load(SFX / "applause_big.ogg")[int(2 * R):int(11 * R)]
    y[:f] *= np.linspace(0, 1, f, dtype=np.float32)
    y[-f:] *= np.linspace(1, 0, f, dtype=np.float32)
    save(y * (0.7 / (np.abs(y).max() or 1)), OUT / "crowd_roar.ogg", q=3)


def music():
    # (Round 10: the darbuka match track is gone at the owner's request; only the menu track is made.)
    for src, dst in [("music_hero.mp3", "menu_hero.ogg")]:
        MUSIC.mkdir(parents=True, exist_ok=True)
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(DL / src), "-ac", "2", "-ar", "44100",
                        "-af", "loudnorm=I=-18:TP=-1.5:LRA=11", "-c:a", "libvorbis", "-q:a", "4", str(MUSIC / dst)], check=True)
        print(f"Music/{dst}")


def pitch(k):
    """An ffmpeg pitch shift by rate (k > 1 higher, shorter)."""
    return f"asetrate=44100*{k},aresample=44100"


def more_field():
    """Round 9: 4–5 takes of every contact sound, so an over never repeats one audibly."""
    one = lambda path, af, secs, **kw: finish(onset(load(path, af)), secs, **kw)
    save(one(QP / "qubodupPunch01.flac", "lowpass=1600", 0.3), OUT / "pad_3.ogg")
    save(one(QP / "qubodupPunch03.flac", pitch(0.9) + ",lowpass=1500", 0.3), OUT / "pad_4.ogg")
    save(one(QI / "qubodupImpactMeat02.flac", pitch(0.85) + ",lowpass=1400", 0.3), OUT / "pad_5.ogg")
    save(one(QP / "qubodupPunch01.flac", pitch(1.1) + ",highpass=200", 0.28), OUT / "glove_3.ogg")
    save(one(QP / "qubodupPunch03.flac", "highpass=180", 0.28), OUT / "glove_4.ogg")
    save(one(QP / "qubodupPunch02.flac", pitch(1.15) + ",highpass=250", 0.28), OUT / "glove_5.ogg")
    save(one(BFH / "bfh1_wood_hit_01.ogg", "lowpass=1000", 0.18), OUT / "bounce_3.ogg")
    save(one(WOOD / "wood2.aif", pitch(0.8) + ",lowpass=800", 0.18), OUT / "bounce_4.ogg")
    save(one(WOOD / "wood16.aif", pitch(0.75) + ",lowpass=700", 0.18), OUT / "bounce_5.ogg")
    save(one(QP / "qubodupPunch04.flac", pitch(0.85) + ",lowpass=600", 0.35), OUT / "rope_3.ogg")
    save(one(QP / "qubodupPunch05.flac", "lowpass=550", 0.35), OUT / "rope_4.ogg")
    save(one(QI / "qubodupImpactMeat02.flac", pitch(0.8) + ",lowpass=500", 0.35), OUT / "rope_5.ogg")
    save(finish(mix((w("wood9"), 1, 0), (onset(load(BFH / "bfh1_wood_hit_02.ogg")), 0.5, 0.04)), 0.55), OUT / "stumps_3.ogg")
    save(one(LJUD / "impactwood11.mp3.flac", "", 0.6, fade=0.15), OUT / "stumps_4.ogg")
    save(finish(mix((w("wood6"), 1, 0), (w("wood13", "highpass=700"), 0.5, 0.06)), 0.5), OUT / "stumps_5.ogg")
    save(one(BFH / "bfh1_wood_falling_02.ogg", pitch(1.15) + ",highpass=500", 0.45, peak=0.7), OUT / "bails_3.ogg")
    save(one(BFH / "bfh1_wood_falling_01.ogg", pitch(0.9) + ",highpass=500", 0.6, peak=0.7, fade=0.15), OUT / "bails_4.ogg")


def stings():
    """DJ stings for boundaries: the loudest 3.5 s stretches of the CC0 menu track, faded."""
    win = int(3.5 * R)
    n = 2  # (sting_1/_2 were cut from the darbuka track, removed in round 10)
    for src in ("music_hero.mp3",):
        x = load(DL / src)
        # Window energy by a running sum (a direct convolution over minutes of audio takes forever).
        cs = np.concatenate([[0.0], np.cumsum(x.astype(np.float64) ** 2)])
        e = np.zeros(len(x))
        e[:len(x) - win] = (cs[win:len(x)] - cs[:len(x) - win]) / win
        picked = []
        for _ in range(2):
            mask = np.ones(len(x), bool)
            for p in picked:
                mask[max(0, p - 12 * R):p + 12 * R] = False
            # (Cheap search: one candidate every 0.25 s, not within 12 s of a stretch already taken.)
            best, at = -1.0, 0
            for i in range(R * 5, len(x) - win - R * 5, R // 4):
                if mask[i] and (v := float(e[i])) > best:
                    best, at = v, i
            picked.append(at)
            y = x[at:at + win].copy()
            f = int(0.08 * R)
            y[:f] *= np.linspace(0, 1, f, dtype=np.float32)
            g = int(1.2 * R)
            y[-g:] *= np.linspace(1, 0, g, dtype=np.float32) ** 2
            n += 1
            save(y * (0.85 / (np.abs(y).max() or 1)), OUT / f"sting_{n}.ogg", q=4)


if __name__ == "__main__":
    # Round 13: the bat takes (bats()) are retired — tools/build_bat_r13.py makes the wooden set — and
    # the PA chime was deleted outright at the owner's request (2026-11-07) — never re-add it.
    if "--round9" in sys.argv:
        more_field(); stings()
    else:
        field(); beds(); music(); more_field(); stings()
