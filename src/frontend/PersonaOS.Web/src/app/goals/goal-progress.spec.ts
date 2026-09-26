import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';

import { Goal } from '../core/goals.service';
import { GoalProgress } from './goal-progress';
import { askGoalProgress } from './goal-progress-dialog';

function goal(overrides: Partial<Goal>): Goal {
  return {
    id: 2, key: 'GOAL-2', title: 'Create a nutrition plan', periodType: 'month', status: 'active',
    progress: 40, effectiveProgress: 40, taskCount: 0, doneTaskCount: 0, totalPoints: 0, donePoints: 0,
    tasks: [], ...overrides,
  } as Goal;
}

@Component({
  imports: [GoalProgress],
  template: `<app-goal-progress [goal]="goal()" />`,
})
class Host {
  readonly goal = signal(goal({}));
}

describe('GoalProgress', () => {
  function render(value: Goal) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.goal.set(value);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('only reads out a goal kept by hand; it is changed from Update progress', () => {
    const page = render(goal({}));

    expect(page.querySelector('input')).toBeNull();
    expect(page.querySelector<HTMLElement>('.fill')!.style.width).toBe('40%');
    expect(page.querySelector('.value')?.textContent).toBe('40%');
  });

  it('reads out a goal whose tasks set its progress, with their points and count', () => {
    const page = render(goal({ taskCount: 2, doneTaskCount: 1, totalPoints: 8, donePoints: 3, effectiveProgress: 38 }));

    expect(page.querySelector<HTMLElement>('.fill')!.style.width).toBe('38%');
    expect(page.textContent?.replace(/\s+/g, ' ')).toContain('38% 3 of 8 points · 1 of 2 tasks');
  });
});

describe('Update progress', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }] });
  });

  async function open(value: Goal) {
    const answer = askGoalProgress(TestBed.inject(MatDialog), value);
    await new Promise(r => setTimeout(r));
    TestBed.tick();
    const dialog = document.querySelector<HTMLElement>('app-goal-progress-dialog')!;
    const slider = dialog.querySelector<HTMLInputElement>('input[type=range]')!;
    const save = [...dialog.querySelectorAll('button')].find(b => b.textContent?.trim() === 'Save')!;
    const cancel = [...dialog.querySelectorAll('button')].find(b => b.textContent?.trim() === 'Cancel')!;
    const move = (to: number) => {
      slider.value = String(to);
      slider.dispatchEvent(new Event('input'));
      TestBed.tick();
    };
    return { answer, dialog, slider, save, cancel, move };
  }

  it('opens on the saved value and saves the new one', async () => {
    const { answer, dialog, slider, save, move } = await open(goal({}));

    expect(dialog.textContent).toContain('GOAL-2');
    expect(slider.step).toBe('5');
    expect(slider.value).toBe('40');
    expect(dialog.querySelector('.value')?.textContent).toBe('40%');
    expect(save.disabled).toBeTrue(); // nothing to save yet

    // The slider's handle once stuck out below it and made the content scroll.
    const content = dialog.querySelector<HTMLElement>('mat-dialog-content')!;
    expect(content.scrollHeight).toBeLessThanOrEqual(content.clientHeight);

    move(75);
    expect(dialog.querySelector('.value')?.textContent).toBe('75%');
    expect(save.disabled).toBeFalse();

    save.click();
    expect(await answer).toBe(75);
  });

  it('saves nothing on Cancel', async () => {
    const { answer, cancel, move } = await open(goal({}));
    move(90);
    cancel.click();
    expect(await answer).toBeNull();
  });
});
