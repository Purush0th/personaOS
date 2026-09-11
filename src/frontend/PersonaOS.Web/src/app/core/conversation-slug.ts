/**
 * Conversation URLs are `/chat/<id>-<slugified title>`, e.g. `/chat/14-what-are-my-goals`.
 *
 * The id leads so a link keeps working when a title changes or contains nothing slugifiable
 * (emoji, CJK, punctuation only) — the readable tail is for humans, never for lookup.
 */

/** Longest readable tail we append; keeps URLs sane for very long first messages. */
const MAX_TAIL = 60;

/** Builds the URL segment for a conversation. */
export function conversationSlug(id: number, title: string | null | undefined): string {
  const tail = slugifyTitle(title ?? '');
  return tail ? `${id}-${tail}` : `${id}`;
}

/**
 * Reads the conversation id from a URL segment, or null when there isn't one.
 * Tolerates a stale or hand-edited tail: only the leading digits matter.
 */
export function conversationIdFromSlug(slug: string | null | undefined): number | null {
  if (!slug) return null;

  const match = /^(\d+)(?:-|$)/.exec(slug);
  if (!match) return null;

  const id = Number(match[1]);
  return Number.isSafeInteger(id) && id > 0 ? id : null;
}

function slugifyTitle(title: string): string {
  return title
    .normalize('NFKD')
    // Strip accents so "Café" becomes "cafe" rather than losing the letter entirely.
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, MAX_TAIL)
    // A trim after slicing, in case the cut landed on a separator.
    .replace(/-+$/g, '');
}
