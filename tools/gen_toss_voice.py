"""The toss's voices with Amazon Polly (same account and voices as gen_commentary.py).

  python tools/gen_toss_voice.py

tools/toss_lines.json -> audio/voice/comm_en/<key>_<n>.mp3, where a line with
{team} is made once per franchise: toss_won_<ID>_1.mp3. They sit in the commentary
folders, which unity_bootstrap.ps1 already mirrors into Resources/Voice, and are played
by name (MatchDirector.PreMatchTossTalk.cs), so commentary.json is left alone.
English only: Kiara presents, Arjun is the captain.
Only new or changed lines are made (audio/voice/toss_texts.json remembers the text).
"""
import json
import os
from concurrent.futures import ThreadPoolExecutor

from gen_commentary import REPO, LANG, polly

VOICES = {'en': {'presenter': 'Kiara', 'captain': 'Arjun'}}


def main():
    lines = json.load(open(os.path.join(REPO, 'tools', 'toss_lines.json'), encoding='utf-8'))
    data = json.load(open(os.path.join(REPO, 'data', 'apl_dataset.json'), encoding='utf-8'))
    teams = {f['id']: f['name'] for f in data['franchises']}
    memo_path = os.path.join(REPO, 'audio', 'voice', 'toss_texts.json')
    old = json.load(open(memo_path, encoding='utf-8')) if os.path.exists(memo_path) else {}
    memo, jobs = {}, []
    for lang in ('en',):
        out_dir = os.path.join(REPO, 'audio', 'voice', 'comm_' + lang)
        os.makedirs(out_dir, exist_ok=True)
        for event, variants in lines[lang].items():
            for i, (role, text) in enumerate(variants, 1):
                per_team = [(f'{event}_{tid}_{i}', text.replace('{team}', name)) for tid, name in teams.items()] \
                    if '{team}' in text else [(f'{event}_{i}', text)]
                for key, said in per_team:
                    memo[f'{lang}/{key}'] = said
                    out = os.path.join(out_dir, key + '.mp3')
                    if os.path.exists(out) and old.get(f'{lang}/{key}') == said:
                        continue
                    jobs.append((said, VOICES[lang][role], LANG[lang], out))
    with ThreadPoolExecutor(6) as pool:
        list(pool.map(lambda j: polly(*j), jobs))
    json.dump(memo, open(memo_path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f'toss: {len(jobs)} clips synthesized, {len(memo)} in all')


if __name__ == '__main__':
    main()
