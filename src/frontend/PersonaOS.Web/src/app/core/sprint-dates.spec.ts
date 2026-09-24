import { Sprint, sprintDayHasCome, sprintEndFor, toDateOnly } from './board.service';

describe('sprint dates', () => {
  it('ends a sprint on the first Sunday after its start day, at 18:00', () => {
    // 2026-09-21 is a Monday; 2026-09-27 a Sunday.
    for (let day = 21; day <= 26; day++) {
      expect(sprintEndFor(new Date(2026, 8, day))).toEqual(new Date(2026, 8, 27, 18, 0));
    }
    expect(sprintEndFor(new Date(2026, 8, 27))).toEqual(new Date(2026, 9, 4, 18, 0));
  });

  it('writes a day the way the API takes it', () => {
    expect(toDateOnly(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05');
  });

  it('lets a sprint start from its first day, at any hour, and not before', () => {
    const sprint = { startsAtUtc: new Date(2026, 8, 27, 20, 0).toISOString() } as Sprint;

    expect(sprintDayHasCome(sprint, new Date(2026, 8, 25, 12, 0))).toBeFalse();
    expect(sprintDayHasCome(sprint, new Date(2026, 8, 26, 23, 59))).toBeFalse();
    expect(sprintDayHasCome(sprint, new Date(2026, 8, 27, 19, 0))).toBeTrue(); // the planning hour
    expect(sprintDayHasCome(sprint, new Date(2026, 9, 1, 9, 0))).toBeTrue();
  });
});
