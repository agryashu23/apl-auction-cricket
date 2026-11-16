/// Deterministic random numbers (mulberry32).
///
/// We don't use `dart:math` Random because its algorithm isn't guaranteed to
/// stay the same across Dart SDK versions. A friend-room season must replay
/// identically from its seed, so we own the generator.
class Rng {
  Rng(int seed) : _state = seed & 0xFFFFFFFF;

  int _state;

  int nextUint32() {
    _state = (_state + 0x6D2B79F5) & 0xFFFFFFFF;
    var t = _state;
    t = _imul(t ^ (t >>> 15), t | 1);
    t ^= (t + _imul(t ^ (t >>> 7), t | 61)) & 0xFFFFFFFF;
    return (t ^ (t >>> 14)) & 0xFFFFFFFF;
  }

  /// Uniform in [0, 1).
  double nextDouble() => nextUint32() / 4294967296.0;

  /// Uniform in [0, max).
  int nextInt(int max) {
    if (max <= 0) throw ArgumentError.value(max, 'max', 'must be positive');
    return (nextDouble() * max).floor();
  }

  bool chance(double p) => nextDouble() < p;

  double range(double min, double max) => min + (max - min) * nextDouble();

  T pick<T>(List<T> items) => items[nextInt(items.length)];

  /// Index into [weights], chosen in proportion to each weight.
  int pickWeighted(List<double> weights) {
    final total = weights.fold(0.0, (s, w) => s + w);
    var roll = nextDouble() * total;
    for (var i = 0; i < weights.length; i++) {
      roll -= weights[i];
      if (roll < 0) return i;
    }
    return weights.length - 1;
  }

  void shuffle<T>(List<T> list) {
    for (var i = list.length - 1; i > 0; i--) {
      final j = nextInt(i + 1);
      final tmp = list[i];
      list[i] = list[j];
      list[j] = tmp;
    }
  }
}

int _imul(int a, int b) => (a * b) & 0xFFFFFFFF;
