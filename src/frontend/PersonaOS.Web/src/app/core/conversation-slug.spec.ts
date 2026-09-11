import { conversationIdFromSlug, conversationSlug } from './conversation-slug';

describe('conversationSlug', () => {
  it('puts the id first and a readable tail after', () => {
    expect(conversationSlug(14, 'What are my goals')).toBe('14-what-are-my-goals');
  });

  it('strips punctuation and collapses separators', () => {
    expect(conversationSlug(3, 'Add "Buy milk" to my planner — today!')).toBe(
      '3-add-buy-milk-to-my-planner-today'
    );
  });

  it('keeps letters when accents are stripped', () => {
    expect(conversationSlug(5, 'Café plans')).toBe('5-cafe-plans');
  });

  it('falls back to the bare id when nothing slugifiable remains', () => {
    expect(conversationSlug(9, '🎯🎯🎯')).toBe('9');
    expect(conversationSlug(9, '')).toBe('9');
    expect(conversationSlug(9, null)).toBe('9');
  });

  it('caps a very long title and never ends on a separator', () => {
    const slug = conversationSlug(1, 'a'.repeat(20) + ' ' + 'b'.repeat(80));

    expect(slug.length).toBeLessThanOrEqual(63);
    expect(slug.endsWith('-')).toBeFalse();
  });
});

describe('conversationIdFromSlug', () => {
  it('reads the leading id', () => {
    expect(conversationIdFromSlug('14-what-are-my-goals')).toBe(14);
  });

  it('accepts a bare id', () => {
    expect(conversationIdFromSlug('14')).toBe(14);
  });

  it('ignores a stale tail, so a renamed conversation still resolves', () => {
    expect(conversationIdFromSlug('14-completely-different-title')).toBe(14);
  });

  it('returns null when there is no usable id', () => {
    expect(conversationIdFromSlug(null)).toBeNull();
    expect(conversationIdFromSlug('')).toBeNull();
    expect(conversationIdFromSlug('what-are-my-goals')).toBeNull();
    expect(conversationIdFromSlug('0-nope')).toBeNull();
    expect(conversationIdFromSlug('-5-negative')).toBeNull();
  });

  it('round-trips with conversationSlug', () => {
    expect(conversationIdFromSlug(conversationSlug(42, 'Some title'))).toBe(42);
    expect(conversationIdFromSlug(conversationSlug(42, '🎯'))).toBe(42);
  });
});
