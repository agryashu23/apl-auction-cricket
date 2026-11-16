#!/usr/bin/env bash
# Hall sound effects from Wikimedia Commons — public domain / CC0 only.
# Downloads the originals to client-assets/sfx/source/, cuts and normalises
# them with ffmpeg into client-src/Assets/Resources/Sfx/<name>.ogg (what
# Sfx.cs loads), and writes client-assets/sfx/CREDITS.md.
#
#   bash tools/fetch_sfx.sh
set -euo pipefail
repo="$(cd "$(dirname "$0")/.." && pwd)"
src="$repo/client-assets/sfx/source"
out="$repo/client-src/Assets/Resources/Sfx"
mkdir -p "$src" "$out"
ua="APL-game-dev/1.0 (sound effects fetch)"

# name | Commons file | licence
sounds=(
  "applause|Applause i.ogg|Public domain"
  "applause_big|Sound Effects - Applause after a concert.ogg|CC0"
  "applause_short|277021 sandermotions applause-2.wav|CC0"
  "cheer|Clapping hurray.ogg|Public domain"
  "ooh|Ohhh ahhh.ogg|Public domain"
  "crowd|Festival concert people crowd.ogg|Public domain"
  "knock|Knocking on wood or door.ogg|Public domain"
)

credits="$repo/client-assets/sfx/CREDITS.md"
{
  echo "# Hall sound effects"
  echo
  echo "All from Wikimedia Commons, public domain or CC0 (no attribution required; listed for provenance)."
  echo
} > "$credits"

for row in "${sounds[@]}"; do
  IFS='|' read -r name file licence <<< "$row"
  ext="${file##*.}"
  raw="$src/$name.$ext"
  if [ ! -s "$raw" ]; then
    url=""
    for attempt in 1 2 3 4 5; do
      url=$(curl -s -G -A "$ua" "https://commons.wikimedia.org/w/api.php" \
        --data-urlencode "action=query" --data-urlencode "titles=File:$file" \
        --data-urlencode "prop=imageinfo" --data-urlencode "iiprop=url" --data-urlencode "format=json" |
        python -c "import sys,json; print(next(iter(json.load(sys.stdin)['query']['pages'].values()))['imageinfo'][0]['url'])" 2>/dev/null) && break
      sleep $((attempt * 5))   # Commons rate-limits bursts
    done
    [ -n "$url" ] || { echo "could not resolve File:$file" >&2; exit 1; }
    curl -s -L --retry 5 --retry-delay 5 -A "$ua" -o "$raw" "$url"
    sleep 3
  fi
  echo "- \`${name/knock/gavel}.ogg\` ← [File:$file](https://commons.wikimedia.org/wiki/File:${file// /_}) — $licence" >> "$credits"
  echo "$name: $(ffprobe -v error -show_entries format=duration -of csv=p=0 "$raw")s"
done

# cut  <name> <start> <length> <fade-out> [extra filters]
cut() {
  local name=$1 start=$2 len=$3 fade=$4 extra=${5:-}
  local raw; raw=$(ls "$src/$name".* | head -1)
  local fo; fo=$(python -c "print(max(0, $len - $fade))")
  ffmpeg -v error -y -ss "$start" -t "$len" -i "$raw" -ac 1 -ar 44100 \
    -af "afade=t=in:d=0.05,afade=t=out:st=$fo:d=$fade,loudnorm=I=-16:TP=-1.5:LRA=11${extra}" \
    -c:a libvorbis -q:a 5 "$out/$name.ogg"
}
cut applause 0 9 2.5
cut applause_big 0 14 3.5
cut applause_short 0 6 1.5
cut cheer 0 9 2
cut ooh 0 4.5 1
# Gavel: the first two knocks, a little lower for a heavier wooden thud.
cut knock 0.18 0.62 0.12 ",asetrate=44100*0.82,aresample=44100,volume=2.2"
mv "$out/knock.ogg" "$out/gavel.ogg"
# One rap for "going once" / "going twice".
cut knock 0.18 0.3 0.1 ",asetrate=44100*0.82,aresample=44100,volume=2.0"
mv "$out/knock.ogg" "$out/gavel_tap.ogg"
# Room tone: long, quiet, soft edges so the loop seam is gentle.
cut crowd 5 40 1.5 ",afade=t=in:d=1.5,volume=0.7"
ls -la "$out"
