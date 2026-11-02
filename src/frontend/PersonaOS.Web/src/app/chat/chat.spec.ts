import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { Chat } from './chat';

describe('Chat', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withXhr()),
        provideHttpClientTesting(),
        provideRouter([
          { path: 'chat', component: Chat },
          { path: 'chat/:slug', component: Chat },
        ]),
      ],
    });
  });

  it('opens the conversation used last', async () => {
    // Owner, 2026-10-11: every visit to the chat started an empty one.
    const harness = await RouterTestingHarness.create('/chat');
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/api/chat/conversations').flush([
      { id: 7, publicId: 'k3n9x2qp', title: 'Groceries', createdAtUtc: '', updatedAtUtc: '' },
      { id: 3, publicId: 'a1b2c3d4', title: 'Older', createdAtUtc: '', updatedAtUtc: '' },
    ]);
    // The list answers through a promise; the navigation it starts follows a turn later.
    await new Promise(resolve => setTimeout(resolve));
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/chat/k3n9x2qp');
    http.expectOne('/api/chat/conversations/k3n9x2qp');
  });

  it('starts an empty chat when asked for a new one', async () => {
    const harness = await RouterTestingHarness.create('/chat?new=1');
    await harness.fixture.whenStable();

    TestBed.inject(HttpTestingController).expectNone('/api/chat/conversations');
    expect(TestBed.inject(Router).url).toBe('/chat');
  });

  it('writes finished replies without the Orb, and spaces each new turn', async () => {
    // Owner, 2026-11-02: the Orb beside every reply was "overuse of the logo".
    const harness = await RouterTestingHarness.create('/chat/k3n9x2qp');
    TestBed.inject(HttpTestingController)
      .expectOne('/api/chat/conversations/k3n9x2qp')
      .flush({
        id: 7,
        publicId: 'k3n9x2qp',
        title: 'Plans',
        mode: 'chat',
        messages: [
          { role: 'user', content: 'What is on today?' },
          { role: 'assistant', content: 'A walk at six.' },
          { role: 'assistant', content: 'And dinner.' },
        ],
      });
    await new Promise(resolve => setTimeout(resolve));
    harness.detectChanges();
    await harness.fixture.whenStable();

    const page = harness.routeNativeElement!;
    const bubbles = Array.from(page.querySelectorAll('.bubble'));
    expect(bubbles.length).toBe(3);
    expect(page.querySelector('.messages .orb')).toBeNull();
    expect(bubbles.map(b => b.classList.contains('turn'))).toEqual([false, true, false]);
  });

  it('fills the composer from a starter that needs finishing', async () => {
    const harness = await RouterTestingHarness.create('/chat?new=1');
    await harness.fixture.whenStable();
    harness.detectChanges();

    const remind = Array.from(harness.routeNativeElement!.querySelectorAll<HTMLButtonElement>('.starters button'))
      .find(b => b.textContent?.trim() === 'Remind me');
    remind!.click();
    harness.detectChanges();
    await harness.fixture.whenStable();

    const box = harness.routeNativeElement!.querySelector('textarea')!;
    expect(box.value).toBe('Remind me to ');
    expect(document.activeElement).toBe(box);
    TestBed.inject(HttpTestingController).expectNone(() => true);
  });
});
