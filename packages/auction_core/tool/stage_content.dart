// Stages a content release in build/content/: the dataset and crests under
// content-addressed names, plus manifest.json pointing at them. Upload them to
// the content bucket files first, manifest last.
//
//   dart run tool/stage_content.dart [--published-version N] [--min-app-build B]
//
// --published-version is the live manifest's dataVersion; staging refuses
// anything that isn't strictly newer, so a release can't go backwards.
import 'dart:convert';
import 'dart:io';

import 'package:auction_core/auction_core.dart';
import 'package:crypto/crypto.dart';

const _dataset = '../../data/apl_dataset.json';
const _logos = '../../data/logos';
const _out = '../../build/content';

void main(List<String> args) {
  int? arg(String name) {
    final i = args.indexOf('--$name');
    return i >= 0 ? int.parse(args[i + 1]) : null;
  }

  final published = arg('published-version');
  final minAppBuild = arg('min-app-build') ?? 1;

  final datasetBytes = File(_dataset).readAsBytesSync();
  final dataset = Dataset.fromJson(jsonDecode(utf8.decode(datasetBytes)) as Map<String, dynamic>);
  final problems = dataset.validate();
  if (problems.isNotEmpty) _fail('Dataset is invalid:\n  ${problems.join('\n  ')}');
  if (dataset.players.any((p) => p.name.startsWith('Player r'))) {
    _fail('Dataset still has placeholder names; rebuild without --allow-placeholder-names.');
  }
  if (published != null && dataset.version <= published) {
    _fail('Dataset version ${dataset.version} is not newer than the live version $published. '
        'Rebuild with: dart run tool/build_dataset.dart --version ${published + 1}');
  }

  final out = Directory(_out);
  if (out.existsSync()) out.deleteSync(recursive: true);

  ContentFile put(String folder, String stem, String ext, List<int> bytes) {
    final hash = sha256.convert(bytes).toString();
    final path = '$folder/$stem.${hash.substring(0, 12)}.$ext';
    File('$_out/$path')
      ..createSync(recursive: true)
      ..writeAsBytesSync(bytes);
    return ContentFile(path: path, sha256: hash, bytes: bytes.length);
  }

  final crests = <String, ContentFile>{};
  for (final f in dataset.franchises) {
    final svg = File('$_logos/${f.id}.svg');
    if (!svg.existsSync()) _fail('Missing crest for ${f.id}; run tool/gen_logos.dart');
    crests[f.id] = put('logos', f.id, 'svg', svg.readAsBytesSync());
  }

  final manifest = ContentManifest(
    dataVersion: dataset.version,
    minAppBuild: minAppBuild,
    publishedAt: DateTime.now().toUtc(),
    dataset: put('data', 'apl_dataset.v${dataset.version}', 'json', datasetBytes),
    crests: crests,
  );
  File('$_out/manifest.json').writeAsStringSync(const JsonEncoder.withIndent('  ').convert(manifest.toJson()));
  stdout.writeln('Staged data v${dataset.version} (${dataset.players.length} players, '
      '${crests.length} crests, minAppBuild $minAppBuild) in build/content');
}

Never _fail(String message) {
  stderr.writeln(message);
  exit(1);
}
