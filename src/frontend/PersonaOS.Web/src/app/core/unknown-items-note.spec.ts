import { unknownItemsNote } from './chat.service';

describe('unknownItemsNote', () => {
  it('says nothing when the reply named only real items', () => {
    expect(unknownItemsNote(null)).toBeNull();
    expect(unknownItemsNote(undefined)).toBeNull();
    expect(unknownItemsNote([])).toBeNull();
  });

  it('names one missing item in the singular', () => {
    expect(unknownItemsNote(['TASK-6'])).toBe(
      'This reply mentions TASK-6, which does not exist. Check before relying on it.'
    );
  });

  it('lists several missing items in the plural', () => {
    expect(unknownItemsNote(['TASK-6', 'GOAL-9', 'SPRINT-4'])).toBe(
      'This reply mentions TASK-6, GOAL-9 and SPRINT-4, which do not exist. Check before relying on it.'
    );
  });
});
