import { conversationRefFromSlug, conversationSlug } from './conversation-slug';

describe('conversationSlug', () => {
  it('uses the public id', () => {
    expect(conversationSlug('k3n9x2qp', 14)).toBe('k3n9x2qp');
  });

  it('falls back to the numeric id when a public id is missing', () => {
    // Defensive: an older API response, or a conversation not yet in the refreshed list.
    expect(conversationSlug(null, 14)).toBe('14');
    expect(conversationSlug(undefined, 14)).toBe('14');
    expect(conversationSlug('', 14)).toBe('14');
    expect(conversationSlug('   ', 14)).toBe('14');
  });

  it('never leaks the title into the URL', () => {
    const slug = conversationSlug('k3n9x2qp', 14);

    expect(slug).not.toContain('-');
    expect(slug.length).toBe(8);
  });
});

describe('conversationRefFromSlug', () => {
  it('reads a public id', () => {
    expect(conversationRefFromSlug('k3n9x2qp')).toBe('k3n9x2qp');
  });

  it('still accepts a numeric id, so old links keep working', () => {
    expect(conversationRefFromSlug('14')).toBe('14');
  });

  it('trims surrounding whitespace', () => {
    expect(conversationRefFromSlug(' k3n9x2qp ')).toBe('k3n9x2qp');
  });

  it('rejects anything that cannot be a reference', () => {
    expect(conversationRefFromSlug(null)).toBeNull();
    expect(conversationRefFromSlug('')).toBeNull();
    expect(conversationRefFromSlug('   ')).toBeNull();
    expect(conversationRefFromSlug('14-what-are-my-goals')).toBeNull();
    expect(conversationRefFromSlug('../../etc/passwd')).toBeNull();
    expect(conversationRefFromSlug('a'.repeat(17))).toBeNull();
  });

  it('round-trips with conversationSlug', () => {
    expect(conversationRefFromSlug(conversationSlug('k3n9x2qp', 14))).toBe('k3n9x2qp');
    expect(conversationRefFromSlug(conversationSlug(null, 14))).toBe('14');
  });
});
