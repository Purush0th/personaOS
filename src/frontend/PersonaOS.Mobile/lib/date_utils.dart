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

/// Parses a timestamp the API calls `...Utc` into a real UTC [DateTime].
///
/// The server serializes these without a trailing `Z` (`2026-09-12T17:30:30.10`),
/// and `DateTime.parse` reads an undesignated timestamp as *local*. East of
/// Greenwich that silently backdates everything by the UTC offset: a document
/// uploaded a second ago was displayed as `5h ago`. Mark it UTC explicitly
/// unless the string already carries a zone.
DateTime parseServerUtc(String value) {
  final hasZone = value.endsWith('Z') ||
      RegExp(r'[+-]\d{2}:?\d{2}$').hasMatch(value);
  return DateTime.parse(hasZone ? value : '${value}Z');
}

/// A short "when did this happen" label for list rows, e.g. `4m ago`, `3d ago`.
///
/// Takes a UTC timestamp from the API and compares it in UTC — comparing a UTC
/// instant against a local `now` would report an offset-sized lie (`5h ago` for
/// something that happened seconds ago, east of Greenwich).
String relativeTime(DateTime utc) {
  final elapsed = DateTime.now().toUtc().difference(utc.toUtc());

  if (elapsed.inMinutes < 1) return 'just now';
  if (elapsed.inMinutes < 60) return '${elapsed.inMinutes}m ago';
  if (elapsed.inHours < 24) return '${elapsed.inHours}h ago';
  if (elapsed.inDays < 7) return '${elapsed.inDays}d ago';
  return localYmd(utc.toLocal());
}

/// `yyyy-MM-ddTHH:mm` in local wall-clock, for reminder due times the server
/// interprets in the install's configured zone.
String localDateTime(DateTime dt) {
  final hour = dt.hour.toString().padLeft(2, '0');
  final minute = dt.minute.toString().padLeft(2, '0');
  return '${localYmd(dt)}T$hour:$minute';
}
