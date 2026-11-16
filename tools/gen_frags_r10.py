"""Round 10: more ways to say the chase ("19 needed from 18 balls", "18 balls left, 19 to get",
"19 runs required, 18 balls remaining") — fragment clips the client stitches round the
number clips (Commentary.SayNeed). Amazon Polly, CLI profile `default`,
Arjun (the lead), English only.

    python -I tools/gen_frags_r10.py
"""
import json
import os
import subprocess
import tempfile
import time

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
OUT = os.path.join(REPO, 'audio', 'voice', 'comm_en')
FRAGS = {
    'frag_needed_from_1': 'needed from',
    'frag_balls_left_1': 'balls left,',
    'frag_ball_left_1': 'ball left,',
    'frag_to_get_1': 'to get.',
    'frag_runs_required_1': 'runs required,',
    'frag_balls_remaining_1': 'balls remaining.',
    'frag_ball_remaining_1': 'ball remaining.',
}


def polly(text, out):
    with tempfile.NamedTemporaryFile('w', suffix='.txt', delete=False, encoding='utf-8') as f:
        f.write(text)
        path = f.name
    try:
        for attempt in range(5):
            r = subprocess.run(['aws', 'polly', 'synthesize-speech', '--profile', 'default', '--region', 'us-east-1',
                                '--engine', 'generative', '--voice-id', 'Arjun', '--language-code', 'en-IN',
                                '--output-format', 'mp3', '--sample-rate', '24000',
                                '--text', 'file://' + path.replace('\\', '/'), out],
                               stdout=subprocess.DEVNULL, env=dict(os.environ, AWS_CLI_FILE_ENCODING='UTF-8'))
            if r.returncode == 0:
                return
            time.sleep(2 + 3 * attempt)
        raise RuntimeError('polly failed: ' + text)
    finally:
        os.remove(path)


def main():
    manifest_path = os.path.join(REPO, 'audio', 'voice', 'commentary.json')
    manifest = json.load(open(manifest_path, encoding='utf-8'))
    for key, text in FRAGS.items():
        out = os.path.join(OUT, key + '.mp3')
        if not os.path.exists(out):
            polly(text, out)
            print(key, text)
        manifest['texts']['en'][key] = text
    json.dump(manifest, open(manifest_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)


if __name__ == '__main__':
    main()
