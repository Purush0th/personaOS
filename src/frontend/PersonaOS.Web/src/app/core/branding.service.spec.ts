import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { BrandingService } from './branding.service';

describe('BrandingService', () => {
  let service: BrandingService;
  let http: HttpTestingController;

  const branding = { assistantNickname: 'Juno', isConfigured: true, enabledFeatures: ['goals'], repository: '' };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideZonelessChangeDetection(), provideHttpClient(withXhr()), provideHttpClientTesting()],
    });
    service = TestBed.inject(BrandingService);
    http = TestBed.inject(HttpTestingController);
  });

  /**
   * Lets the failed request settle and the service's retry delay (0 ms in these tests) pass, so
   * its next request is out. One timer tick is not enough: the retry's own timer is only set
   * once the failure has worked its way through the promise chain.
   */
  const nextTick = () => new Promise(resolve => setTimeout(resolve, 20));

  it('keeps trying while the server is still starting, then loads', async () => {
    const loading = service.load([0, 0]);

    http.expectOne('/api/branding').flush(null, { status: 502, statusText: 'Bad Gateway' });
    await nextTick();
    http.expectOne('/api/branding').flush(branding);
    await loading;

    expect(service.branding()?.assistantNickname).toBe('Juno');
    expect(service.loadError()).toBeNull();
    expect(service.isEnabled('goals')).toBeTrue();
  });

  it('says so when the server stays unreachable, and a later try clears it', async () => {
    const failing = service.load([0]);
    http.expectOne('/api/branding').flush(null, { status: 502, statusText: 'Bad Gateway' });
    await nextTick();
    http.expectOne('/api/branding').flush(null, { status: 502, statusText: 'Bad Gateway' });
    await failing;

    expect(service.loadError()).toBe('Could not reach the PersonaOS server.');

    const retry = service.load([]);
    expect(service.loadError()).toBeNull();
    http.expectOne('/api/branding').flush(branding);
    await retry;

    expect(service.branding()).not.toBeNull();
  });
});
