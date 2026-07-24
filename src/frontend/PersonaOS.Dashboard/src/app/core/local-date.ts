/** Formats a Date as `yyyy-MM-dd` in the *local* calendar.
 *
 * `toISOString()` converts to UTC first, so anywhere east of Greenwich a local
 * midnight lands on the previous UTC day and the date silently shifts back one.
 * Planner days are calendar days, never instants — always format them locally. */
export function toLocalDate(value: Date): string {
  const month = `${value.getMonth() + 1}`.padStart(2, '0');
  const day = `${value.getDate()}`.padStart(2, '0');
  return `${value.getFullYear()}-${month}-${day}`;
}

/** Parses a `yyyy-MM-dd` calendar day into local midnight and shifts it by `days`. */
export function shiftLocalDate(date: string, days: number): string {
  const shifted = new Date(`${date}T00:00:00`);
  shifted.setDate(shifted.getDate() + days);
  return toLocalDate(shifted);
}

export function todayLocal(): string {
  return toLocalDate(new Date());
}
