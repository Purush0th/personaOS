import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, UrlSegment, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { AuthService } from './core/auth.service';
import { GoalDetail } from './goals/goal-detail';
import { PAGE_ROUTES, chatUrl } from './pages.routes';
import { Timeline } from './timeline/timeline';

describe('PAGE_ROUTES', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(PAGE_ROUTES),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { isLoggedIn: () => true } },
      ],
    });
  });

  it('gives a goal its own address under /goals, not under the board', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/goals/GOAL-2', GoalDetail);

    expect(page).toBeInstanceOf(GoalDetail);
    expect(TestBed.inject(Router).url).toBe('/goals/GOAL-2');
  });

  it('keeps the Timeline as a tab of Goals, not a goal called "timeline"', async () => {
    const harness = await RouterTestingHarness.create();
    const page = await harness.navigateByUrl('/timeline', Timeline);

    expect(page).toBeInstanceOf(Timeline);
    expect(TestBed.inject(Router).url).toBe('/goals/timeline');
  });

  it('sends the old /board/goals address to the new one', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/board/goals/GOAL-2');

    expect(TestBed.inject(Router).url).toBe('/goals/GOAL-2');
  });
});

describe('chatUrl', () => {
  const segments = (...paths: string[]) => paths.map(p => new UrlSegment(p, {}));

  it('matches /chat and /chat/<slug> as one route, so the page is kept when a thread gets its address', () => {
    expect(chatUrl(segments('chat'))?.posParams).toBeUndefined();
    expect(chatUrl(segments('chat', 'k3n9x2qp'))?.posParams?.['slug'].path).toBe('k3n9x2qp');
  });

  it('leaves every other address to its own route', () => {
    expect(chatUrl(segments('goals'))).toBeNull();
    expect(chatUrl(segments('chat', 'a', 'b'))).toBeNull();
  });
});
