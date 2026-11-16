// Draws a crest for every franchise in data/source/league.json:
// shared shield, team colours from the data, one emblem per team.
//
//   dart run tool/gen_logos.dart   ->  data/logos/<ID>.svg + preview.html
//
// Original designs; re-run after changing a team's colours.
import 'dart:convert';
import 'dart:io';

const _league = '../../data/source/league.json';
const _outDir = '../../data/logos';

const _shield =
    'M256 24 L456 84 V256 C456 372 368 452 256 492 C144 452 56 372 56 256 V84 Z';
const _innerShield =
    'M256 56 L426 108 V256 C426 356 352 424 256 458 C160 424 86 356 86 256 V108 Z';

/// Emblem markup per franchise id. `INK` = main emblem colour, `ACC` = accent,
/// `BG` = shield fill (for cut-outs).
const _emblems = <String, String>{
  // Mumbai Heroes: star
  'MBH': '<path d="M256 130 L284.2 211.2 L370.1 212.9 L301.7 264.8 L326.5 347.1 '
      'L256 298 L185.5 347.1 L210.3 264.8 L141.9 212.9 L227.8 211.2 Z" fill="INK"/>'
      '<circle cx="256" cy="250" r="26" fill="ACC"/>',
  // Chennai Cyclones: spiral
  'CHC': '<path d="M236 250 A20 20 0 1 1 276 250 A40 40 0 1 1 196 250 '
      'A60 60 0 1 1 316 250 A80 80 0 1 1 156 250 A100 100 0 1 1 356 250" '
      'fill="none" stroke="INK" stroke-width="22" stroke-linecap="round"/>',
  // Bengaluru Royals: crown
  'BLR': '<path d="M156 310 L140 170 L206 232 L256 148 L306 232 L372 170 L356 310 Z" '
      'fill="INK" stroke-linejoin="round"/>'
      '<rect x="150" y="318" width="212" height="34" rx="8" fill="INK"/>'
      '<circle cx="140" cy="164" r="15" fill="ACC"/><circle cx="256" cy="140" r="17" fill="ACC"/>'
      '<circle cx="372" cy="164" r="15" fill="ACC"/><circle cx="256" cy="268" r="18" fill="ACC"/>',
  // Kolkata Comets: comet
  'KOC': '<path d="M258 176 L118 318 L292 244 Z" fill="INK" opacity="0.55"/>'
      '<path d="M270 196 L150 370 L304 258 Z" fill="INK" opacity="0.8"/>'
      '<circle cx="306" cy="208" r="56" fill="INK"/>'
      '<circle cx="320" cy="194" r="18" fill="ACC"/>',
  // Delhi Defenders: castle tower
  'DLD': '<rect x="176" y="204" width="160" height="160" fill="INK"/>'
      '<rect x="176" y="168" width="34" height="40" fill="INK"/>'
      '<rect x="239" y="168" width="34" height="40" fill="INK"/>'
      '<rect x="302" y="168" width="34" height="40" fill="INK"/>'
      '<path d="M234 364 V312 A22 22 0 0 1 278 312 V364 Z" fill="BG"/>'
      '<rect x="210" y="236" width="20" height="34" rx="10" fill="ACC"/>'
      '<rect x="282" y="236" width="20" height="34" rx="10" fill="ACC"/>',
  // Punjab Pioneers: compass rose
  'PJP': '<circle cx="256" cy="250" r="96" fill="none" stroke="INK" stroke-width="10"/>'
      '<path d="M256 118 L278 228 L388 250 L278 272 L256 382 L234 272 L124 250 L234 228 Z" fill="INK"/>'
      '<path d="M256 118 L278 228 L256 250 L234 228 Z" fill="ACC"/>'
      '<circle cx="256" cy="250" r="12" fill="BG"/>',
  // Rajasthan Rangers: sun over dunes
  'RJR': '<g stroke="INK" stroke-width="12" stroke-linecap="round">'
      '<path d="M256 112 V136"/><path d="M256 304 V328"/><path d="M148 220 H172"/>'
      '<path d="M340 220 H364"/><path d="M180 144 L197 161"/><path d="M315 279 L332 296"/>'
      '<path d="M332 144 L315 161"/><path d="M197 279 L180 296"/></g>'
      '<circle cx="256" cy="220" r="62" fill="INK"/>'
      '<path d="M100 352 Q180 286 262 334 T420 316 L420 400 L100 400 Z" fill="ACC"/>',
  // Hyderabad Hawks: hawk head in profile
  'HYH': '<path d="M160 344 C150 240 222 160 318 160 C352 160 384 178 402 204 L346 214 '
      'C356 244 344 272 314 286 L258 304 C236 322 204 346 160 344 Z" fill="INK"/>'
      '<path d="M402 204 L346 214 L364 240 Z" fill="ACC"/>'
      '<circle cx="318" cy="204" r="13" fill="BG"/>',
  // Gujarat Gladiators: crossed swords behind a round shield
  'GJG': '<g stroke="INK" stroke-width="18" stroke-linecap="round">'
      '<path d="M150 132 L362 372"/><path d="M362 132 L150 372"/></g>'
      '<g stroke="ACC" stroke-width="14" stroke-linecap="round">'
      '<path d="M310 352 L352 318"/><path d="M202 352 L160 318"/></g>'
      '<circle cx="256" cy="252" r="74" fill="INK"/>'
      '<circle cx="256" cy="252" r="46" fill="none" stroke="BG" stroke-width="8"/>'
      '<circle cx="256" cy="252" r="16" fill="ACC"/>',
  // Lucknow Lancers: crossed lances with pennants
  'LKL': '<g stroke="INK" stroke-width="14" stroke-linecap="round">'
      '<path d="M160 384 L340 150"/><path d="M352 384 L172 150"/></g>'
      '<path d="M352 128 L368 156 L346 168 L334 142 Z" fill="INK"/>'
      '<path d="M160 128 L178 142 L166 168 L144 156 Z" fill="INK"/>'
      '<path d="M318 178 L364 196 L322 214 Z" fill="ACC"/>'
      '<path d="M194 178 L148 196 L190 214 Z" fill="ACC"/>',
};

void main() {
  final league = jsonDecode(File(_league).readAsStringSync()) as Map<String, dynamic>;
  Directory(_outDir).createSync(recursive: true);
  final cards = StringBuffer();
  for (final f in (league['franchises'] as List).cast<Map<String, dynamic>>()) {
    final id = f['id'] as String;
    final primary = f['primaryColor'] as String;
    final secondary = f['secondaryColor'] as String;
    final emblem = _emblems[id];
    if (emblem == null) {
      stderr.writeln('No emblem for $id; add one to _emblems.');
      exit(1);
    }
    // Light shields get a dark emblem so it stays readable.
    final light = _luminance(primary) > 0.55;
    final ink = light ? secondary : '#FFFFFF';
    final accent = light ? '#FFFFFF' : secondary;
    final svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">'
        '<defs><clipPath id="inner"><path d="$_innerShield"/></clipPath></defs>'
        '<path d="$_shield" fill="$primary" stroke="$secondary" stroke-width="18" stroke-linejoin="round"/>'
        '<path d="$_innerShield" fill="none" stroke="$ink" stroke-opacity="0.35" stroke-width="4"/>'
        '<g clip-path="url(#inner)">'
        '${emblem.replaceAll('INK', ink).replaceAll('ACC', accent).replaceAll('BG', primary)}'
        '</g></svg>';
    File('$_outDir/$id.svg').writeAsStringSync(svg);
    cards.write('<figure><img src="$id.svg" alt="${f['name']}"><figcaption>${f['name']}</figcaption></figure>');
  }
  File('$_outDir/preview.html').writeAsStringSync(
    '<!doctype html><meta charset="utf-8"><title>APL crests</title>'
    '<style>body{font:15px system-ui;background:#f4f4f6;margin:24px}'
    'main{display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:20px}'
    'figure{margin:0;text-align:center;background:#fff;border-radius:12px;padding:16px}'
    'img{width:110px;height:110px}figcaption{margin-top:8px}</style>'
    '<h1>APL crests</h1><main>$cards</main>',
  );
  stdout.writeln('Wrote ${(league['franchises'] as List).length} crests to $_outDir');
}

double _luminance(String hex) {
  final v = int.parse(hex.substring(1), radix: 16);
  final r = ((v >> 16) & 0xFF) / 255;
  final g = ((v >> 8) & 0xFF) / 255;
  final b = (v & 0xFF) / 255;
  return 0.299 * r + 0.587 * g + 0.114 * b;
}
