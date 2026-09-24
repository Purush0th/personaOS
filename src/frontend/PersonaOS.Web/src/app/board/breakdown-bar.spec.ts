import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { BreakdownBar, BreakdownPart } from './breakdown-bar';

@Component({
  imports: [BreakdownBar],
  template: `<app-breakdown-bar [parts]="parts()" />`,
})
class Host {
  readonly parts = signal<BreakdownPart[]>([
    { label: 'This week', count: 3, tone: 's-todo' },
    { label: 'In progress', count: 0, tone: 's-in_progress' },
    { label: 'Done', count: 1, tone: 's-done' },
  ]);
}

describe('BreakdownBar', () => {
  function render() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('sizes each part by its count and leaves empty parts out of the bar', () => {
    const parts = [...render().querySelectorAll<HTMLElement>('.bar .part')];
    expect(parts.map(p => [p.className.match(/s-\w+/)?.[0], p.style.flexGrow])).toEqual([
      ['s-todo', '3'],
      ['s-done', '1'],
    ]);
  });

  it('lists every part in the legend, zeros included, and reads out as one sentence', () => {
    const page = render();
    expect([...page.querySelectorAll('li')].map(li => li.textContent?.replace(/\s+/g, ' ').trim()))
      .toEqual(['This week 3', 'In progress 0', 'Done 1']);
    expect(page.querySelector('.bar')?.getAttribute('aria-label')).toBe('This week 3, In progress 0, Done 1');
  });
});
