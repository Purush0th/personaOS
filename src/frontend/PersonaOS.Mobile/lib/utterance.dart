/// Whether what was heard stops mid-sentence: "Can you find me the tasks that..." (owner's chat
/// b4c7pvc2, 2026-11-04), where the speaker paused to think and the pause ended the turn. Hands-free
/// keeps listening and joins the next words on instead of sending half a question.
bool soundsUnfinished(String words) {
  final text = words.trim();
  if (text.isEmpty) return false;
  if (text.endsWith('...') || text.endsWith('…') || text.endsWith(',')) return true;
  final last = RegExp(r"([A-Za-z']+)[.!?]?$").firstMatch(text)?.group(1)?.toLowerCase();
  return last != null && _leadsOn.contains(last);
}

/// Words a finished sentence does not end on.
const _leadsOn = {
  'a',
  'about',
  'an',
  'and',
  'are',
  'as',
  'at',
  'because',
  'but',
  'by',
  'for',
  'from',
  'if',
  'in',
  'into',
  'is',
  'like',
  'my',
  'of',
  'on',
  'or',
  'so',
  'than',
  'that',
  'the',
  'their',
  'then',
  'to',
  'when',
  'which',
  'who',
  'whose',
  'with',
  'your',
  'our',
  'his',
  'her',
  'its',
  'until',
  'while',
  'where',
  'whether',
  'also',
  'um',
  'uh',
};

/// The words heard so far and the words just heard, as one utterance.
String joinUtterance(String before, String after) {
  final head = before.trim().replaceFirst(RegExp(r'(\.\.\.|…)$'), '').trimRight();
  final tail = after.trim();
  if (head.isEmpty) return tail;
  if (tail.isEmpty) return head;
  return '$head $tail';
}
