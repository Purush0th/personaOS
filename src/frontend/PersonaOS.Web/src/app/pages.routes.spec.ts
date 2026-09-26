import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { AuthService } from './core/auth.service';
import { GoalDetail } from './goals/goal-detail';
import { PAGE_ROUTES } from './pages.routes';

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

  it('sends the old /board/goals address to the new one', async () => {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/board/goals/GOAL-2');

    expect(TestBed.inject(Router).url).toBe('/goals/GOAL-2');
  });
});
