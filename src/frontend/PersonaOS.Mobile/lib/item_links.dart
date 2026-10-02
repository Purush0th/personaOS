/// Item keys the assistant writes in its replies (TASK-7, GOAL-4) and the links that open them.
library;

/// The scheme of a link to an item: `personaos:TASK-7`.
const itemLinkScheme = 'personaos';

/// A key on its own: not part of a longer word or key, a path, a link's address (`:TASK-7`) or
/// an existing link's text (`[TASK-7](...)`).
final _itemKey = RegExp(r'(?<![\w\[/:-])(TASK|GOAL)-\d+(?![\w-]|\]\()');

final _wholeKey = RegExp(r'^(TASK|GOAL)-\d+$');

/// Turns every task and goal key in [markdown] into a link to that item, except inside code and
/// existing links, and except the keys in [unknown]: those the server could not find, which must
/// not look like something that opens.
String linkItemKeys(String markdown, {Iterable<String> unknown = const []}) {
  final skip = unknown.toSet();
  final parts = markdown.split('`');
  for (var i = 0; i < parts.length; i += 2) {
    // Even parts are outside code spans; odd parts are code and stay as written.
    parts[i] = parts[i].replaceAllMapped(_itemKey, (m) {
      final key = m[0]!;
      return skip.contains(key) ? key : '[$key]($itemLinkScheme:$key)';
    });
  }
  return parts.join('`');
}

/// The item key a link points at, or null when it is not a link to an item.
String? itemKeyFromLink(String? href) {
  if (href == null || !href.startsWith('$itemLinkScheme:')) return null;
  final key = href.substring(itemLinkScheme.length + 1);
  return _wholeKey.hasMatch(key) ? key : null;
}
