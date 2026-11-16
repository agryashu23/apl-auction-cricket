"""RETIRED in round 13: the owner asked for the stadium PA ("airport-like") and its chime to go;
the game no longer plays them and Resources/Sfx/pa was deleted. Kept for the record only — do not run.
"""
"""Round 9: the stadium PA announcer (Amazon Polly, CLI profile `default`).

    python -I tools/gen_pa_r9.py

Each line is synthesised (Matthew, generative, en-US), then made to sound like a
ground's PA with ffmpeg — band-limited to a horn speaker, two slap-back echoes off
the stands — into client-src/Assets/Resources/Sfx/pa/<key>.ogg (MatchSfx.Announce
plays the chime, then one of an event's lines). English only. Existing files are kept.
"""
import os
import subprocess
import tempfile
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
OUT = os.path.join(REPO, 'client-src', 'Assets', 'Resources', 'Sfx', 'pa')
VOICE = 'Matthew'

LINES = {
    'pa_welcome': ["Ladies and gentlemen, welcome to tonight's A P L match!", "Good evening, and welcome to the ground. Enjoy the cricket!"],
    'pa_four': ["That's a four!", "Four runs!", "Boundary!"],
    'pa_six': ["That's a six!", "Maximum!", "Six runs! Into the crowd!"],
    'pa_wicket': ["That's out!", "Wicket!", "And he's gone!"],
    'pa_new_batter': ["Please welcome the next batter to the crease.", "Next in to bat. Make some noise!"],
    'pa_timeout': ["Strategic time out."],
    'pa_last_over': ["Last over of the innings. Let's hear it!"],
    'pa_result': ["What a match! Thank you for being with us tonight.", "That's the match! Please stay for the presentation."],
}


def synth(text, mp3):
    with tempfile.NamedTemporaryFile('w', suffix='.txt', delete=False, encoding='utf-8') as f:
        f.write(text)
        path = f.name
    try:
        for attempt in range(5):
            r = subprocess.run(['aws', 'polly', 'synthesize-speech', '--profile', 'default', '--region', 'us-east-1',
                                '--engine', 'generative', '--voice-id', VOICE, '--language-code', 'en-US',
                                '--output-format', 'mp3', '--sample-rate', '24000',
                                '--text', 'file://' + path.replace('\\', '/'), mp3],
                               stdout=subprocess.DEVNULL, env=dict(os.environ, AWS_CLI_FILE_ENCODING='UTF-8'))
            if r.returncode == 0:
                return
            time.sleep(2 + 3 * attempt)
        raise RuntimeError('polly failed for ' + text)
    finally:
        os.remove(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    work = tempfile.mkdtemp()
    made = 0
    for key, texts in LINES.items():
        for i, text in enumerate(texts, 1):
            out = os.path.join(OUT, f'{key}_{i}.ogg')
            if os.path.exists(out):
                continue
            mp3 = os.path.join(work, f'{key}_{i}.mp3')
            synth(text, mp3)
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', mp3, '-ac', '1', '-ar', '44100', '-af',
                            'highpass=f=280,lowpass=f=4200,acompressor=threshold=-18dB:ratio=4,'
                            'aecho=0.8:0.6:140|290:0.32|0.18,apad=pad_dur=0.4,loudnorm=I=-16:TP=-1.5',
                            '-c:a', 'libvorbis', '-q:a', '4', out], check=True)
            print(f'{key}_{i}: {text}')
            made += 1
    print(f'PA: {made} lines made')


if __name__ == '__main__':
    main()
