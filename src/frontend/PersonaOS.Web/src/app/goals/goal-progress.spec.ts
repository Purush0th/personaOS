import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { Goal } from '../core/goals.service';
import { GoalProgress } from './goal-progress';

function goal(overrides: Partial<Goal>): Goal {
  return {
    id: 2, key: 'GOAL-2', title: 'Create a nutrition plan', periodType: 'month', status: 'active',
    progress: 40, effectiveProgress: 40, taskCount: 0, doneTaskCount: 0, totalPoints: 0, donePoints: 0,
    tasks: [], ...overrides,
  } as Goal;
}

@Component({
  imports: [GoalProgress],
  template: `<app-goal-progress [goal]="goal()" (changed)="saved.push($event)" />`,
})
class Host {
  readonly goal = signal(goal({}));
  readonly saved: number[] = [];
}

describe('GoalProgress', () => {
  function render(value: Goal) {
    const fixture = TestBed.createComponent(Host);
    fixture.componentInstance.goal.set(value);
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  it('makes the bar itself the control for a goal kept by hand, in 5% steps', () => {
    const { fixture, page } = render(goal({}));
    const slider = page.querySelector<HTMLInputElement>('input[type=range]')!;

    expect(slider.step).toBe('5');
    expect(slider.value).toBe('40');
    expect(page.querySelector('input[type=number]')).toBeNull();

    slider.value = '75';
    slider.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    expect(page.querySelector('.value')?.textContent).toBe('75%'); // follows the thumb while dragging
    expect(fixture.componentInstance.saved).toEqual([]);

    slider.dispatchEvent(new Event('change'));
    expect(fixture.componentInstance.saved).toEqual([75]); // saved once it is let go
  });

  it('does not save a value that did not change', () => {
    const { fixture, page } = render(goal({}));
    page.querySelector('input')!.dispatchEvent(new Event('change'));
    expect(fixture.componentInstance.saved).toEqual([]);
  });

  it('only reads out a goal whose tasks set its progress', () => {
    const { page } = render(goal({ taskCount: 2, doneTaskCount: 1, totalPoints: 8, donePoints: 3, effectiveProgress: 38 }));

    expect(page.querySelector('input')).toBeNull();
    expect(page.querySelector<HTMLElement>('.fill')!.style.width).toBe('38%');
    expect(page.textContent?.replace(/\s+/g, ' ')).toContain('38% 3 of 8 points · 1 of 2 tasks');
  });
});
