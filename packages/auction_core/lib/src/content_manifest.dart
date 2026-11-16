/// One downloadable file on the content CDN. [path] is content-addressed
/// (contains part of the hash), so it never changes once published and can be
/// cached forever; [sha256] lets the app verify what it downloaded.
class ContentFile {
  const ContentFile({required this.path, required this.sha256, required this.bytes});

  final String path;
  final String sha256;
  final int bytes;

  factory ContentFile.fromJson(Map<String, dynamic> j) => ContentFile(
        path: j['path'] as String,
        sha256: j['sha256'] as String,
        bytes: j['bytes'] as int,
      );

  Map<String, dynamic> toJson() => {'path': path, 'sha256': sha256, 'bytes': bytes};
}

/// `manifest.json` at the CDN root: the one small file the app checks on
/// launch to learn whether newer game data exists.
class ContentManifest {
  const ContentManifest({
    required this.dataVersion,
    required this.minAppBuild,
    required this.publishedAt,
    required this.dataset,
    required this.crests,
  });

  static const schema = 1;

  /// Same as the dataset's `version`; only ever goes up.
  final int dataVersion;

  /// Older app builds ignore this data (it may need features they lack).
  final int minAppBuild;
  final DateTime publishedAt;
  final ContentFile dataset;

  /// Franchise id → crest SVG.
  final Map<String, ContentFile> crests;

  factory ContentManifest.fromJson(Map<String, dynamic> j) {
    final s = j['schema'];
    if (s != schema) throw FormatException('Unsupported manifest schema $s');
    return ContentManifest(
      dataVersion: j['dataVersion'] as int,
      minAppBuild: j['minAppBuild'] as int,
      publishedAt: DateTime.parse(j['publishedAt'] as String),
      dataset: ContentFile.fromJson(j['dataset'] as Map<String, dynamic>),
      crests: {
        for (final e in (j['crests'] as Map<String, dynamic>).entries)
          e.key: ContentFile.fromJson(e.value as Map<String, dynamic>),
      },
    );
  }

  Map<String, dynamic> toJson() => {
        'schema': schema,
        'dataVersion': dataVersion,
        'minAppBuild': minAppBuild,
        'publishedAt': publishedAt.toUtc().toIso8601String(),
        'dataset': dataset.toJson(),
        'crests': {for (final e in crests.entries) e.key: e.value.toJson()},
      };
}
