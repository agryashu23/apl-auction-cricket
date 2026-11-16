"""On-field voices with Amazon Polly (same account as gen_commentary.py): the batters'
running calls, the fielders' appeals, the keeper's chatter.

  python tools/gen_match_voices.py

Six male English voices (generative engine), two per side so the batters differ; each
says every line. mp3 from Polly -> trimmed, mono, loudness-normalised ogg (ffmpeg) in
client-src/Assets/Resources/Sfx/calls/<voice>_<key>.ogg, read by MatchSfx.cs.
Only missing clips are made.
"""
import os
import subprocess
import tempfile
from concurrent.futures import ThreadPoolExecutor

from gen_commentary import REPO, polly

# (voice, language): Indian, American, British, Australian accents.
VOICES = [('Arjun', 'en-IN'), ('Stephen', 'en-US'), ('Brian', 'en-GB'), ('Matthew', 'en-US'),
          ('Russell', 'en-AU'), ('Curtis', 'en-AU')]

LINES = {
    # Running between the wickets.
    'call_yes': 'Yes!', 'call_yes2': 'Yes, yes, run!', 'call_no': 'No!', 'call_no2': 'No, no, get back!',
    'call_wait': 'Wait!', 'call_two': 'Two!', 'call_three': 'Three!', 'call_push': 'Push, push!',
    'call_come': 'Come on!', 'call_back': 'Back! Back!',
    # Appeals.
    'appeal_1': 'Howzat!', 'appeal_2': "How's that!", 'appeal_3': 'Owzaaat!',
    # The keeper and the fielders between balls.
    'chat_bowling': 'Bowling!', 'chat_well': 'Well bowled!', 'chat_areas': 'Good areas, mate!',
    'chat_boys': 'Come on, boys!', 'chat_tight': 'Nice and tight!', 'chat_shot': 'Shot, bowler!',
}


def make(voice, lang, key, text, out_dir):
    out = os.path.join(out_dir, f'{voice.lower()}_{key}.ogg')
    if os.path.exists(out):
        return False
    with tempfile.TemporaryDirectory() as tmp:
        mp3 = os.path.join(tmp, 'line.mp3')
        polly(text, voice, lang, mp3)
        # Trim the silence either end, mono, a little louder and brighter (it's shouted outdoors).
        subprocess.run(['ffmpeg', '-y', '-loglevel', 'error', '-i', mp3, '-af',
                        'silenceremove=start_periods=1:start_threshold=-45dB,areverse,'
                        'silenceremove=start_periods=1:start_threshold=-45dB,areverse,'
                        'highpass=f=120,loudnorm=I=-14', '-ac', '1', '-c:a', 'libvorbis', '-q:a', '5', out],
                       check=True)
    return True


def main():
    out_dir = os.path.join(REPO, 'client-src', 'Assets', 'Resources', 'Sfx', 'calls')
    os.makedirs(out_dir, exist_ok=True)
    jobs = [(v, lang, k, t, out_dir) for v, lang in VOICES for k, t in LINES.items()]
    with ThreadPoolExecutor(6) as pool:
        made = sum(pool.map(lambda j: make(*j), jobs))
    print(f'match voices: {made} made, {len(jobs)} in all')


if __name__ == '__main__':
    main()
