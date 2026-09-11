import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      // The shell injects BrandingService (HttpClient) and uses routerLink, so both
      // have to be provided — without them the component cannot even be constructed.
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });

  it('creates the app shell', () => {
    const fixture = TestBed.createComponent(App);

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('shows the fixed product name', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    // "PersonaOS" is the one piece of branding that never varies per install.
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('PersonaOS');
  });
});
