import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BurndownPoint } from '../core/board.service';
import { BurndownChart } from './burndown-chart';

@Component({
  imports: [BurndownChart],
  template: `<app-burndown-chart [points]="points()" />`,
})
class Host {
  readonly points = signal<BurndownPoint[]>([
    { date: '2026-09-23', remainingPoints: 10, completedPoints: 0 },
    { date: '2026-09-24', remainingPoints: 4, completedPoints: 6 },
    { date: '2026-09-25', remainingPoints: 0, completedPoints: 10 },
  ]);
}

const day = (iso: string) =>
  new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, { day: 'numeric', month: 'short' });

describe('BurndownChart', () => {
  function render(points?: BurndownPoint[]) {
    const fixture = TestBed.createComponent(Host);
    if (points) fixture.componentInstance.points.set(points);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('places a dot a day, from the top of the scale down to the axis, across the width', () => {
    const page = render();
    const dots = [...page.querySelectorAll<HTMLElement>('.dot')];

    expect(dots.map(d => [d.style.left, d.style.top])).toEqual([
      ['0%', '0%'],
      ['50%', '60%'],
      ['100%', '100%'],
    ]);
    expect(page.querySelector('path.burn')?.getAttribute('d')).toBe('M0.00,0.00 L50.00,60.00 L100.00,100.00');
    expect(dots[1].title).toBe(`${day('2026-09-24')}: 4 points left`);
  });

  it('labels the scale and the first and last day, and reads out as a sentence', () => {
    const page = render();
    const text = (selector: string) => page.querySelector(selector)?.textContent?.trim();

    expect([text('.y.top'), text('.y.bottom')]).toEqual(['10', '0']);
    expect([text('.x.first'), text('.x.last')]).toEqual([day('2026-09-23'), day('2026-09-25')]);
    expect(page.querySelector('.chart')?.getAttribute('aria-label')).toBe(
      `Burndown: 10 points left on ${day('2026-09-23')}, 0 on ${day('2026-09-25')}`
    );
  });

  it('keeps its text at reading size however wide it is drawn', () => {
    const page = render();
    document.body.appendChild(page);
    const size = () => getComputedStyle(page.querySelector('.x.first')!).fontSize;

    page.style.width = '300px';
    const narrow = size();
    page.style.width = '1400px';
    expect(size()).toBe(narrow);
    page.remove();
  });

  it('says what the line is, in words that fit a finished sprint as well as a running one', () => {
    expect(render().querySelector('.caption')?.textContent?.trim()).toBe('Points left at the end of each day');
  });

  it('waits for a second day instead of drawing a single dot', () => {
    const page = render([{ date: '2026-09-23', remainingPoints: 5, completedPoints: 0 }]);
    expect(page.querySelector('.chart')).toBeNull();
    expect(page.textContent).toContain('once the sprint has run for a day');
  });
});
