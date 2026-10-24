import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { PushConfig } from './push-config';

describe('PushConfig', () => {
  let http: HttpTestingController;

  async function render(): Promise<{ el: HTMLElement; settle: () => Promise<void> }> {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(PushConfig);
    fixture.detectChanges();
    http.expectOne('/api/setup/push').flush({ configured: true, projectId: 'personaos-test' });
    const settle = async () => {
      await fixture.whenStable();
      fixture.detectChanges();
    };
    await settle();
    return { el: fixture.nativeElement as HTMLElement, settle };
  }

  it('sends a test notification and says how each phone took it', async () => {
    // Owner, 2026-10-24: the only way to check push was to set a reminder and wait.
    const { el, settle } = await render();

    (el.querySelector('[data-testid="push-test"]') as HTMLButtonElement).click();
    http.expectOne('/api/setup/push/test').flush({
      configured: true,
      delivered: 1,
      devices: [
        { name: 'Pixel 8', delivered: true, error: null, removed: false },
        { name: 'Old phone', delivered: false, error: 'Requested entity was not found.', removed: true },
      ],
    });
    await settle();

    const result = el.querySelector('[data-testid="push-test-result"]')!.textContent!.replace(/\s+/g, ' ');
    expect(result).toContain('Sent to 1 of 2 phone(s)');
    expect(result).toContain('Pixel 8: delivered');
    expect(result).toContain('no longer registered');
  });

  it('says when no phone is registered', async () => {
    const { el, settle } = await render();

    (el.querySelector('[data-testid="push-test"]') as HTMLButtonElement).click();
    http.expectOne('/api/setup/push/test').flush({ configured: true, delivered: 0, devices: [] });
    await settle();

    expect(el.querySelector('[data-testid="push-test-result"]')!.textContent).toContain('No phone is registered');
  });
});
