import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';

import { InlineCreate, NewTask } from './inline-create';

@Component({
  imports: [InlineCreate],
  template: `
    <app-inline-create [allowedPoints]="[1, 2, 3]" [save]="save" (cancelled)="cancelled.set(true)" />
  `,
})
class Host {
  readonly saved: NewTask[] = [];
  readonly cancelled = signal(false);
  /** Whether the next save succeeds. */
  succeed = true;
  readonly save = async (task: NewTask): Promise<boolean> => {
    this.saved.push(task);
    return this.succeed;
  };
}

const settle = () => new Promise(resolve => setTimeout(resolve));

describe('InlineCreate', () => {
  async function render() {
    TestBed.configureTestingModule({
      imports: [Host],
      providers: [{ provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } }],
    });
    const fixture = TestBed.createComponent(Host);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    const field: HTMLTextAreaElement = fixture.nativeElement.querySelector('textarea');
    const type = (text: string) => {
      field.value = text;
      field.dispatchEvent(new Event('input'));
    };
    const press = (key: string, shiftKey = false) => {
      const event = new KeyboardEvent('keydown', { key, shiftKey, bubbles: true, cancelable: true });
      field.dispatchEvent(event);
      return event;
    };
    return { fixture, host: fixture.componentInstance, field, type, press };
  }

  it('takes the cursor as soon as it opens', async () => {
    const { field } = await render();
    expect(document.activeElement).toBe(field);
  });

  it('creates on Enter and stays open, empty, for the next task', async () => {
    const { host, field, type, press } = await render();

    type('  Renew the passport ');
    press('Enter');
    await settle();

    expect(host.saved).toEqual([{ title: 'Renew the passport', points: null }]);
    expect(field.value).toBe('');
    expect(document.activeElement).toBe(field);
  });

  it('keeps what was typed when the save does not go through', async () => {
    const { host, field, type, press } = await render();
    host.succeed = false;

    type('Declined scope change');
    press('Enter');
    await settle();

    expect(host.saved.length).toBe(1);
    expect(field.value).toBe('Declined scope change');
  });

  it('leaves Shift+Enter to the field, and ignores a blank title', async () => {
    const { host, type, press } = await render();

    type('First line');
    expect(press('Enter', true).defaultPrevented).toBeFalse();
    type('   ');
    press('Enter');
    await settle();

    expect(host.saved).toEqual([]);
  });

  it('sends the points picked from its menu', async () => {
    const { fixture, host, type, press } = await render();

    (fixture.nativeElement.querySelector('.points-pill') as HTMLElement).click();
    await fixture.whenStable();
    const three = [...document.querySelectorAll<HTMLElement>('.mat-mdc-menu-item')]
      .find(item => item.textContent?.trim() === '3 points')!;
    three.click();
    type('Estimated');
    press('Enter');
    await settle();

    expect(host.saved).toEqual([{ title: 'Estimated', points: 3 }]);
  });

  it('gives up on Esc', async () => {
    const { host, press } = await render();
    press('Escape');
    expect(host.cancelled()).toBeTrue();
  });
});
