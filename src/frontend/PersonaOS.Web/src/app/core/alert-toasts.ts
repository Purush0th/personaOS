import { Injectable, NgZone, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';

import { parseUtc } from './board.service';
import { BrandingService } from './branding.service';
import { shiftLocalDate, todayLocal } from './local-date';
import { PlannerDay, PlannerService } from './planner.service';
import { ReminderDto, RemindersService } from './reminders.service';

/** Something to tell the user at a moment: a reminder coming due, an event starting. */
export interface Alert {
  /** One per alert and occurrence, so each is shown once. */
  key: string;
  /** When, in epoch milliseconds. */
  at: number;
  text: string;
  /** Where "Open" goes. */
  link: string[];
  query?: Record<string, string>;
}

/** "10 min", "1 hour", "2 days": how far ahead a heads-up comes. */
export function aheadLabel(minutes: number): string {
  if (minutes % 1440 === 0) return minutes === 1440 ? '1 day' : `${minutes / 1440} days`;
  if (minutes % 60 === 0) return minutes === 60 ? '1 hour' : `${minutes / 60} hours`;
  return `${minutes} min`;
}

/**
 * The alerts the phone gets as notifications: a pending reminder when it comes due, and for each
 * planned event a notification when it starts (an all-day one at 12 AM) plus the heads-up before
 * it when one is set. Planner times are local calendar times, read in the browser's zone like the
 * rest of the web planner; reminders carry their exact UTC moment.
 */
export function upcomingAlerts(reminders: ReminderDto[], days: PlannerDay[]): Alert[] {
  const alerts: Alert[] = [];
  for (const reminder of reminders) {
    if (reminder.status !== 'pending') continue;
    alerts.push({
      key: `reminder-${reminder.id}-${reminder.dueAtUtc}`,
      // The server sends UTC without a zone suffix; read bare, it would be taken as local time.
      at: parseUtc(reminder.dueAtUtc).getTime(),
      text: `Reminder: ${reminder.message}`,
      link: ['/reminders'],
    });
  }

  for (const day of days) {
    for (const item of day.items) {
      // An event lasting several days is listed on each day it covers; it starts on its first.
      if (item.status !== 'planned' || item.date !== day.date) continue;
      const start = new Date(`${item.date}T${item.scheduledTime ?? '00:00:00'}`).getTime();
      const key = `event-${item.id}-${item.occurrenceDate}`;
      const query = { view: 'day', date: item.date };
      alerts.push({
        key: `${key}-start`,
        at: start,
        text: item.scheduledTime ? `Starting now: ${item.title}` : `Today: ${item.title}`,
        link: ['/planner'],
        query,
      });
      if (item.notifyBeforeMinutes && item.notifyBeforeMinutes > 0) {
        alerts.push({
          key: `${key}-before`,
          at: start - item.notifyBeforeMinutes * 60_000,
          text: `In ${aheadLabel(item.notifyBeforeMinutes)}: ${item.title}`,
          link: ['/planner'],
          query,
        });
      }
    }
  }
  return alerts;
}

/** How often the reminders and the planner are read again. */
const REFRESH_MS = 5 * 60_000;
/** Alerts further ahead than this wait for a later read, which may have changed them. */
const HORIZON_MS = REFRESH_MS + 60_000;
/** A tab the computer put to sleep still shows what came due this recently. */
const CATCH_UP_MS = 5 * 60_000;
const TOAST_MS = 15_000;

/**
 * Toasts in the web app for what the phone notifies: reminders coming due and events starting
 * (owner, 2026-10-11). Runs while the user is signed in and the app is open; nothing is sent
 * when the tab is closed. Alerts already past when the app opens are not shown again.
 */
@Injectable({ providedIn: 'root' })
export class AlertToasts {
  private readonly reminders = inject(RemindersService);
  private readonly planner = inject(PlannerService);
  private readonly branding = inject(BrandingService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);

  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly shown = new Set<string>();
  private readonly queue: Alert[] = [];
  private poll: ReturnType<typeof setInterval> | null = null;
  private showing = false;
  private firstRead = true;

  private readonly onVisible = () => {
    if (document.visibilityState === 'visible') void this.refresh();
  };

  start(): void {
    if (this.poll) return;
    this.firstRead = true;
    // Outside Angular's zone: the timers wait minutes at a time and change nothing on screen
    // until a toast opens, which goes back in. Inside, the app would never count as settled.
    this.zone.runOutsideAngular(() => {
      this.poll = setInterval(() => void this.refresh(), REFRESH_MS);
      void this.refresh();
      document.addEventListener('visibilitychange', this.onVisible);
    });
  }

  stop(): void {
    if (this.poll) clearInterval(this.poll);
    this.poll = null;
    document.removeEventListener('visibilitychange', this.onVisible);
    this.clearTimers();
    this.queue.length = 0;
  }

  private async refresh(): Promise<void> {
    const today = todayLocal();
    const [reminders, days] = await Promise.all([
      this.branding.isEnabled('reminders') ? this.reminders.list().catch(() => []) : Promise.resolve([]),
      this.branding.isEnabled('planner')
        ? this.planner.getRange(today, shiftLocalDate(today, 1)).catch(() => [])
        : Promise.resolve([]),
    ]);
    if (!this.poll) return; // Signed out while reading.

    const now = Date.now();
    this.clearTimers();
    for (const alert of upcomingAlerts(reminders, days)) {
      if (this.shown.has(alert.key)) continue;
      const wait = alert.at - now;
      if (wait < 0 && (this.firstRead || wait < -CATCH_UP_MS)) {
        this.shown.add(alert.key); // Already past: the phone told the user, or it is too late.
        continue;
      }
      if (wait > HORIZON_MS) continue;
      this.timers.set(alert.key, setTimeout(() => this.zone.run(() => this.enqueue(alert)), Math.max(0, wait)));
    }
    this.firstRead = false;
  }

  private clearTimers(): void {
    for (const timer of this.timers.values()) clearTimeout(timer);
    this.timers.clear();
  }

  private enqueue(alert: Alert): void {
    this.timers.delete(alert.key);
    if (this.shown.has(alert.key)) return;
    this.shown.add(alert.key);
    this.queue.push(alert);
    this.showNext();
  }

  /** One toast at a time: a second would replace the first before it was read. */
  private showNext(): void {
    if (this.showing) return;
    const alert = this.queue.shift();
    if (!alert) return;

    this.showing = true;
    const toast = this.snackBar.open(alert.text, 'Open', { duration: TOAST_MS });
    toast.onAction().subscribe(() => void this.router.navigate(alert.link, { queryParams: alert.query }));
    toast.afterDismissed().subscribe(() => {
      this.showing = false;
      this.showNext();
    });
  }
}
