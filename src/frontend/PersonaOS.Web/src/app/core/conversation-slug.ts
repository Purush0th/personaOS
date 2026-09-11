/**
 * Conversation URLs are `/chat/<publicId>`, e.g. `/chat/k3n9x2qp`.
 *
 * The id is an opaque 8-character code assigned by the server, deliberately not the title
 * (which would leak what was discussed into history, bookmarks and proxy logs) and not the
 * sequential row id (which would advertise how many conversations exist).
 *
 * Numeric ids are still accepted so links made before public ids existed keep working — the
 * API resolves either form.
 */

/** Longest id we will carry in a URL; keeps obviously-bogus input out of API calls. */
const MAX_LENGTH = 16;

/** The URL segment for a conversation. */
export function conversationSlug(publicId: string | null | undefined, id?: number): string {
  return publicId?.trim() || String(id ?? '');
}

/**
 * The conversation reference from a URL segment, or null when the segment cannot be one.
 * Returned verbatim: the server owns the format, so the client does not second-guess it
 * beyond rejecting empty or absurd values.
 */
export function conversationRefFromSlug(slug: string | null | undefined): string | null {
  if (!slug) return null;

  const trimmed = slug.trim();
  if (!trimmed || trimmed.length > MAX_LENGTH) return null;

  // Public ids are alphanumeric; legacy links are digits. Anything else is not a reference.
  return /^[a-zA-Z0-9]+$/.test(trimmed) ? trimmed : null;
}
