import { aheadLabel, upcomingAlerts } from './alert-toasts';
import { PlannerItemDto } from './planner.service';
import { ReminderDto } from './reminders.service';

function reminder(fields: Partial<ReminderDto>): ReminderDto {
  return {
    id: 1,
    message: 'Buy milk',
    dueAtUtc: '2026-10-12T03:30:00Z',
    dueAtLocal: '2026-10-12 09:00',
    status: 'pending',
    deliveredAtUtc: null,
    goalId: null,
    goalTitle: null,
    plannerItemId: null,
    plannerItemTitle: null,
    createdAtUtc: '2026-10-11T10:00:00Z',
    ...fields,
  };
}

function event(fields: Partial<PlannerItemDto>): PlannerItemDto {
  return {
    id: 5,
    title: 'Standup',
    notes: null,
    date: '2026-10-12',
    endDate: null,
    scheduledTime: '09:30:00',
    endTime: '10:00:00',
    sortOrder: 0,
    status: 'planned',
    goalId: null,
    goalTitle: null,
    goalKey: null,
    taskId: null,
    taskKey: null,
    notifyBeforeMinutes: null,
    recurrence: null,
    recurrenceText: null,
    occurrenceDate: '2026-10-12',
    isChanged: false,
    createdAtUtc: '2026-10-11T10:00:00Z',
    updatedAtUtc: '2026-10-11T10:00:00Z',
    ...fields,
  };
}

describe('upcomingAlerts', () => {
  it('alerts a pending reminder at its due moment', () => {
    const [alert] = upcomingAlerts([reminder({})], []);

    expect(alert.text).toBe('Reminder: Buy milk');
    expect(alert.at).toBe(Date.parse('2026-10-12T03:30:00Z'));
    expect(alert.link).toEqual(['/reminders']);
  });

  it('reads a due time sent without a zone as UTC', () => {
    // The API sends "2026-10-11T15:37:37"; read as local time it was hours in the past in India.
    const [alert] = upcomingAlerts([reminder({ dueAtUtc: '2026-10-12T03:30:00' })], []);

    expect(alert.at).toBe(Date.parse('2026-10-12T03:30:00Z'));
  });

  it('skips reminders that are no longer pending', () => {
    expect(upcomingAlerts([reminder({ status: 'delivered' }), reminder({ status: 'cancelled' })], [])).toEqual([]);
  });

  it('alerts every planned event when it starts, like the phone', () => {
    const alerts = upcomingAlerts([], [{ date: '2026-10-12', items: [event({})] }]);

    expect(alerts.map(a => a.text)).toEqual(['Starting now: Standup']);
    expect(alerts[0].at).toBe(new Date('2026-10-12T09:30:00').getTime());
    expect(alerts[0].query).toEqual({ view: 'day', date: '2026-10-12' });
  });

  it('adds the heads-up before an event when one is set', () => {
    const alerts = upcomingAlerts([], [{ date: '2026-10-12', items: [event({ notifyBeforeMinutes: 10 })] }]);

    const before = alerts.find(a => a.key.endsWith('-before'))!;
    expect(before.text).toBe('In 10 min: Standup');
    expect(before.at).toBe(new Date('2026-10-12T09:20:00').getTime());
  });

  it('starts an all-day event at 12 AM', () => {
    const [alert] = upcomingAlerts([], [{ date: '2026-10-12', items: [event({ scheduledTime: null, endTime: null, title: 'Holiday' })] }]);

    expect(alert.text).toBe('Today: Holiday');
    expect(alert.at).toBe(new Date('2026-10-12T00:00:00').getTime());
  });

  it('alerts an event lasting several days only on its first day', () => {
    const trip = event({ date: '2026-10-11', endDate: '2026-10-13', title: 'Trip' });

    expect(upcomingAlerts([], [{ date: '2026-10-12', items: [trip] }])).toEqual([]);
  });

  it('leaves out events that are done or skipped', () => {
    const items = [event({ status: 'done' }), event({ id: 6, status: 'skipped' })];

    expect(upcomingAlerts([], [{ date: '2026-10-12', items }])).toEqual([]);
  });
});

describe('aheadLabel', () => {
  it('says minutes, hours or days', () => {
    expect(aheadLabel(25)).toBe('25 min');
    expect(aheadLabel(60)).toBe('1 hour');
    expect(aheadLabel(480)).toBe('8 hours');
    expect(aheadLabel(1440)).toBe('1 day');
  });
});
