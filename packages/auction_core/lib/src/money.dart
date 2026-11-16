/// All money in the game is whole lakhs (1 Cr = 100 L), so there is no
/// floating-point drift in purses.
const lakhsPerCrore = 100;

/// "₹2 Cr", "₹12.25 Cr", "₹75 L".
String formatLakhs(int lakhs) {
  final sign = lakhs < 0 ? '-' : '';
  final abs = lakhs.abs();
  if (abs < lakhsPerCrore) return '$sign₹$abs L';
  final crores = abs / lakhsPerCrore;
  var text = crores.toStringAsFixed(2);
  text = text.replaceFirst(RegExp(r'\.?0+$'), '');
  return '$sign₹$text Cr';
}
