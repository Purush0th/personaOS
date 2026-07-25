/// Formats a [DateTime] as `yyyy-MM-dd` using its *local* calendar fields.
///
/// Never derive a planner/reminder day from a UTC conversion: a local midnight
/// east of Greenwich maps to the previous UTC day, silently shifting the date
/// back one. Planner days are calendar days, so read the local fields directly.
String localYmd(DateTime date) {
  final month = date.month.toString().padLeft(2, '0');
  final day = date.day.toString().padLeft(2, '0');
  return '${date.year}-$month-$day';
}

/// `yyyy-MM-ddTHH:mm` in local wall-clock, for reminder due times the server
/// interprets in the install's configured zone.
String localDateTime(DateTime dt) {
  final hour = dt.hour.toString().padLeft(2, '0');
  final minute = dt.minute.toString().padLeft(2, '0');
  return '${localYmd(dt)}T$hour:$minute';
}
