import { TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { provideRouter } from '@angular/router';

import { AuthService } from '../core/auth.service';
import { BrandingService } from '../core/branding.service';
import { Login } from './login';

const settle = () => new Promise(resolve => setTimeout(resolve, 20));

describe('Login', () => {
  it('says so when the password is wrong', async () => {
    const auth = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    auth.login.and.resolveTo('Incorrect username or password.');
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: BrandingService, useValue: { branding: () => null } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    const fixture = TestBed.createComponent(Login);
    fixture.componentInstance.username = 'purush';
    fixture.componentInstance.password = 'wrong-pass';

    await fixture.componentInstance.submit();
    fixture.detectChanges();
    await settle();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent?.trim()).toBe('Incorrect username or password.');
  });

  it('asks for both fields before trying', async () => {
    const auth = jasmine.createSpyObj<AuthService>('AuthService', ['login']);
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: auth },
        { provide: BrandingService, useValue: { branding: () => null } },
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    });
    const fixture = TestBed.createComponent(Login);

    await fixture.componentInstance.submit();
    fixture.detectChanges();

    expect(auth.login).not.toHaveBeenCalled();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Enter your username and password.');
  });
});
