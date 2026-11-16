"""Round 13 (owner: "the bat sounds like a steel bat"): the bat sounds rebuilt from the owner's
wooden knock (client-assets/sfx/owner_r13/cricket-bat-hitting-sound.mp3), in three kinds,
three takes each, into client-src/Assets/Resources/Sfx/bat/:

  edge_1..3    a thin, short, dull nick: band-limited (no ring), pitched up, cut short, quieter
  stroke_1..3  a solid, crisp wooden tock: metallic highs cut, low-mids warmed, a short decay
  loft_1..3    a deeper, fuller, louder crack: pitched down, layered with an octave-ish
               lower copy, low end warmed, a longer decay

Every take gets a fast exponential envelope after the hit so nothing rings on (the ring is
what read as steel). Run with -I (the sample came from Downloads: untrusted data):

    python -I tools/build_bat_r13.py
"""
import pathlib
import site
import subprocess
import sys

sys.path.append(site.getusersitepackages())
import numpy as np  # noqa: E402

REPO = pathlib.Path(__file__).resolve().parent.parent
SRC = REPO / "client-assets" / "sfx" / "owner_r13" / "cricket-bat-hitting-sound.mp3"
OUT = REPO / "client-src" / "Assets" / "Resources" / "Sfx" / "bat"
R = 44100


def load(af=""):
    cmd = ["ffmpeg", "-v", "error", "-i", str(SRC), "-ac", "1", "-ar", str(R)]
    if af:
        cmd += ["-af", af]
    cmd += ["-f", "f32le", "-"]
    x = np.frombuffer(subprocess.run(cmd, capture_output=True, check=True).stdout, dtype=np.float32).copy()
    # Start on the hit (the file has ~90 ms of lead-in).
    peak = np.abs(x).max() or 1
    i = int(np.argmax(np.abs(x) > peak * 0.08))
    return x[max(0, i - int(0.002 * R)):]


def shape(x, seconds, decay, peak):
    """Cut to length, a fast exponential fall after the first 6 ms (no ring), a click-free end, normalised."""
    n = int(seconds * R)
    y = np.zeros(n, np.float32)
    y[:min(n, len(x))] = x[:n]
    t = np.arange(n) / R
    env = np.where(t < 0.006, 1.0, np.exp(-(t - 0.006) * decay)).astype(np.float32)
    y *= env
    f = int(0.004 * R)
    y[-f:] *= np.linspace(1, 0, f, dtype=np.float32)
    return y * (peak / (np.abs(y).max() or 1))


def save(y, name):
    OUT.mkdir(parents=True, exist_ok=True)
    path = OUT / f"{name}.ogg"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "f32le", "-ar", str(R), "-ac", "1", "-i", "-",
                    "-c:a", "libvorbis", "-q:a", "6", str(path)], input=y.astype(np.float32).tobytes(), check=True)
    print(f"{path.relative_to(REPO)}  {len(y) / R:.2f}s")


def pitch(k):
    return f"asetrate={R}*{k},aresample={R}"


# EQ: no metal — everything above ~4–5 kHz rolled off, the low-mids (the wood's body) lifted.
WOOD = "lowpass=f=4800,equalizer=f=420:t=q:w=1.0:g=4,equalizer=f=2600:t=q:w=1.2:g=-4"


def main():
    # EDGE: thin and dull — the body cut (highpass), the top cut too, up in pitch, very short, quiet.
    for i, (k, hp, secs) in enumerate([(1.22, 900, 0.07), (1.32, 1100, 0.06), (1.15, 800, 0.08)], 1):
        save(shape(load(f"{pitch(k)},highpass=f={hp},lowpass=f=3200"), secs, 70, 0.55), f"edge_{i}")
    # MIDDLED STROKE: the solid tock — warmed, crisp, a short clean decay.
    for i, (k, secs, decay) in enumerate([(1.0, 0.16, 38), (0.96, 0.17, 34), (1.04, 0.15, 42)], 1):
        save(shape(load(f"{pitch(k)},{WOOD}"), secs, decay, 0.85), f"stroke_{i}")
    # MIDDLED LOFT: deeper and fuller — pitched down, a lower copy layered under it, more body, longer.
    for i, (k, under, secs, decay) in enumerate([(0.86, 0.72, 0.28, 20), (0.82, 0.68, 0.30, 18), (0.9, 0.75, 0.26, 22)], 1):
        a = load(f"{pitch(k)},{WOOD},bass=g=4:f=180")
        b = load(f"{pitch(under)},lowpass=f=2400")
        n = max(len(a), len(b) + int(0.003 * R))
        y = np.zeros(n, np.float32)
        y[:len(a)] += a
        o = int(0.003 * R)
        y[o:o + len(b)] += 0.55 * b
        save(shape(y, secs, decay, 0.97), f"loft_{i}")


if __name__ == "__main__":
    main()
