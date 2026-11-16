"""Round 11: which commentator says each line — "lead" (Arjun) or "colour" (Kiara) — into
audio/voice/commentary.json as "roles" (en), so the client can play the colour lines on
the second commentator's own channel (Commentary.cs). No synthesis; reads the line
files in gen_commentary.py's order (an event's own lines, then commentary_more_en_1, _2, _3, _4)
and checks each against the manifest's text before trusting it.

    python -I tools/tag_roles_r11.py
"""
import glob
import json
import os

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))


def main():
    path = os.path.join(REPO, 'audio', 'voice', 'commentary.json')
    manifest = json.load(open(path, encoding='utf-8'))
    lines = json.load(open(os.path.join(REPO, 'tools', 'commentary_lines.json'), encoding='utf-8'))['en']
    for more in sorted(glob.glob(os.path.join(REPO, 'tools', 'commentary_more_en_*.json'))):
        for event, variants in json.load(open(more, encoding='utf-8')).items():
            if event != 'about':
                lines.setdefault(event, []).extend(variants)
    texts = manifest['texts']['en']
    roles, ok, bad = {}, 0, []
    for event, variants in lines.items():
        for i, (role, text) in enumerate(variants, 1):
            key = f'{event}_{i}'
            if key not in texts:
                continue
            # The stored text is the line without its name slot (and before a mid-line name).
            plain = text.split('{')[0].strip(' ,') if text.strip().startswith('{') is False else text.split('}', 1)[1].strip(' ,')
            if texts[key].strip() and (texts[key] in text or plain[:12] in texts[key] or texts[key][:12] in text):
                roles[key] = role
                ok += 1
            else:
                bad.append(key)
    manifest.setdefault('roles', {})['en'] = roles
    json.dump(manifest, open(path, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    colour = sum(1 for r in roles.values() if r == 'colour')
    print(f'roles: {ok} lines tagged ({colour} colour), {len(bad)} unmatched: {bad[:10]}')


if __name__ == '__main__':
    main()
