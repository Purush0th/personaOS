import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { App } from './app';

describe('App', () => {
  afterEach(() => localStorage.removeItem('personaos.token'));

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      // The shell injects BrandingService (HttpClient) and uses routerLink, so both
      // have to be provided — without them the component cannot even be constructed.
      providers: [provideHttpClient(withXhr()), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('creates the app shell', () => {
    const fixture = TestBed.createComponent(App);

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows the fixed product name once signed in', async () => {
    localStorage.setItem('personaos.token', 'test-token');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    TestBed.inject(HttpTestingController)
      .expectOne('/api/branding')
      .flush({ assistantNickname: 'Juno', isConfigured: true, enabledFeatures: [], repository: '' });
    await fixture.whenStable();
    fixture.detectChanges();

    // "PersonaOS" is the one piece of branding that never varies per install.
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('PersonaOS');
  });

  it('lists chat history under Chat in the nav', async () => {
    localStorage.setItem('personaos.token', 'test-token');
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/branding')
      .flush({ assistantNickname: 'Juno', isConfigured: true, enabledFeatures: [], repository: '' });
    await fixture.whenStable();
    fixture.detectChanges();
    http.expectOne('/api/chat/conversations').flush([
      { id: 1, publicId: 'abc12345', title: 'Plan my week', createdAtUtc: '', updatedAtUtc: '' },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const thread = (fixture.nativeElement as HTMLElement).querySelector('.history a[href="/chat/abc12345"]');
    expect(thread?.textContent).toContain('Plan my week');
  });
});
