import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { UpdatesView } from '../core/updates.service';
import { UpdatesConfig } from './updates-config';

const ALIVE = { heartbeat: '', requestId: null, version: null, state: 'idle', message: null, finishedAt: null };

function view(fields: Partial<UpdatesView>): UpdatesView {
  return {
    currentVersion: '0.1.0-alpha.9',
    latest: {
      version: '0.1.0-alpha.10',
      name: 'Alpha 10',
      notesUrl: 'https://github.com/x/y/releases/tag/v0.1.0-alpha.10',
      prerelease: true,
      publishedAt: '2026-10-24T05:00:00Z',
      appDownloadUrl: null,
    },
    updateAvailable: true,
    autoInstall: false,
    canInstall: true,
    updater: ALIVE,
    ...fields,
  };
}

describe('UpdatesConfig', () => {
  let http: HttpTestingController;

  async function render(answer: UpdatesView): Promise<HTMLElement> {
    TestBed.configureTestingModule({ providers: [provideHttpClient(withXhr()), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(UpdatesConfig);
    fixture.detectChanges();
    http.expectOne('/api/updates').flush(answer);
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('offers the newer release and asks the server to install it', async () => {
    const el = await render(view({}));

    const button = el.querySelector('[data-testid="update-now"]') as HTMLButtonElement;
    expect(button.textContent).toContain('Update to 0.1.0-alpha.10');
    button.click();

    const request = http.expectOne('/api/updates/install');
    expect(request.request.body).toEqual({ version: '0.1.0-alpha.10' });
  });

  it('says how to turn the updater on when there is none', async () => {
    const el = await render(view({ canInstall: false, updater: null }));

    expect(el.querySelector('[data-testid="update-now"]')).toBeNull();
    expect(el.querySelector('[data-testid="manual-update"]')?.textContent).toContain('COMPOSE_PROFILES');
  });

  it('shows how far an update has got', async () => {
    const el = await render(view({ updater: { ...ALIVE, state: 'pulling', version: '0.1.0-alpha.10' } }));

    expect(el.querySelector('[data-testid="update-progress"]')?.textContent).toContain('Downloading');
  });

  it('says plainly when an update was rolled back', async () => {
    const el = await render(
      view({ updater: { ...ALIVE, state: 'rolled-back', message: '0.1.0-alpha.10 did not answer. Back on 0.1.0-alpha.9.' } })
    );

    expect(el.querySelector('[data-testid="update-problem"]')?.textContent).toContain('Back on 0.1.0-alpha.9');
  });
});
