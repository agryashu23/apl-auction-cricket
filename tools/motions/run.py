"""
Run one of the generator scripts in lib/ inside Blender, as if from the command line:

    blender -b --python tools/motions/run.py -- <script.py> [args...]

Blender keeps its own arguments in sys.argv; everything after "--" is the
script's, so it sees a plain argv the way the packs' __main__ blocks expect.
"""
import os
import runpy
import sys

here = os.path.dirname(os.path.abspath(__file__))
lib = os.path.join(here, "lib")
args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if not args:
    sys.exit("usage: blender -b --python run.py -- <script.py> [args...]")
script = args[0]
if not os.path.isabs(script):
    script = os.path.join(lib, script) if os.path.exists(os.path.join(lib, script)) else os.path.join(here, script)
sys.path.insert(0, lib)
sys.argv = [script] + args[1:]
runpy.run_path(script, run_name="__main__")
