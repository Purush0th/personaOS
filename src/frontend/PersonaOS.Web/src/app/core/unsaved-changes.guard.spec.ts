import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, RouterStateSnapshot } from '@angular/router';

import { Confirm } from './confirm';
import { HasUnsavedChanges, unsavedChangesGuard } from './unsaved-changes.guard';

describe('unsavedChangesGuard', () => {
  let asked: number;
  let answer: boolean;

  beforeEach(() => {
    asked = 0;
    TestBed.configureTestingModule({
      providers: [{ provide: Confirm, useValue: { ask: () => (asked++, Promise.resolve(answer)) } }],
    });
  });

  const leave = (dirty: boolean) =>
    TestBed.runInInjectionContext(() =>
      unsavedChangesGuard(
        { hasUnsavedChanges: () => dirty } as HasUnsavedChanges,
        {} as ActivatedRouteSnapshot,
        {} as RouterStateSnapshot,
        {} as RouterStateSnapshot,
      ),
    );

  it('lets a page without edits go without asking', async () => {
    expect(await leave(false)).toBeTrue();
    expect(asked).toBe(0);
  });

  it('asks before leaving unsaved edits, and stays when the user says so', async () => {
    answer = false;
    expect(await leave(true)).toBeFalse();
    answer = true;
    expect(await leave(true)).toBeTrue();
    expect(asked).toBe(2);
  });
});
