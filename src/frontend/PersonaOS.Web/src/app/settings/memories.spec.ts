import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter } from '@angular/router';

import { Confirm } from '../core/confirm';
import { Memory, MemoriesService } from '../core/memories.service';
import { Memories } from './memories';

function memory(id: number, content: string, category: Memory['category'], conversation: string | null = null): Memory {
  return {
    id, content, category,
    sourceConversationId: conversation, sourceConversationTitle: conversation ? 'Workout chat' : null,
    createdAtUtc: '2026-09-29T00:00:00Z', updatedAtUtc: '2026-09-29T00:00:00Z',
  };
}

const settle = () => new Promise(resolve => setTimeout(resolve, 50));

describe('Memories', () => {
  let api: jasmine.SpyObj<MemoriesService>;
  let confirm: jasmine.SpyObj<Confirm>;

  async function render(): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Memories);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const rows = (el: HTMLElement) => Array.from(el.querySelectorAll('.memory .content')).map(e => e.textContent?.trim());

  beforeEach(() => {
    api = jasmine.createSpyObj<MemoriesService>('MemoriesService', ['list', 'getAutoSave', 'setAutoSave', 'create', 'update', 'delete']);
    api.list.and.resolveTo([
      memory(1, 'Prefers morning workouts', 'preference', 'abcd1234'),
      memory(2, 'Car insurance renews in March', 'fact'),
    ]);
    api.getAutoSave.and.resolveTo(true);
    api.delete.and.resolveTo();
    confirm = jasmine.createSpyObj<Confirm>('Confirm', ['error', 'ask', 'done']);

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: MemoriesService, useValue: api },
        { provide: Confirm, useValue: confirm },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
  });

  it('lists every memory with its category and the chat it came from', async () => {
    const el = await render();

    expect(rows(el)).toEqual(['Prefers morning workouts', 'Car insurance renews in March']);
    const link = el.querySelector('.memory a') as HTMLAnchorElement;
    expect(link.getAttribute('href')).toBe('/chat/abcd1234');
    expect(el.textContent).toContain('Preference');
  });

  it('says what auto-save does', async () => {
    const el = await render();

    expect(el.textContent).toContain('saves a memory when you tell it something worth keeping');
  });

  it('deletes a memory only after asking', async () => {
    confirm.ask.and.resolveTo(true);
    const fixture = TestBed.createComponent(Memories);
    fixture.detectChanges();
    await settle();
    fixture.detectChanges();

    (fixture.nativeElement.querySelector('button[aria-label="Delete"]') as HTMLButtonElement).click();
    await settle();
    fixture.detectChanges();

    expect(confirm.ask).toHaveBeenCalled();
    expect(api.delete).toHaveBeenCalledWith(1);
    expect(rows(fixture.nativeElement)).toEqual(['Car insurance renews in March']);
  });

  it('shows how to start when there are none', async () => {
    api.list.and.resolveTo([]);
    const el = await render();

    expect(el.textContent).toContain('Nothing yet');
  });
});
