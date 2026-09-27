import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Session } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { LoginComponent } from './login.component';

const session: Session = {
  accessToken: 'token-value',
  expiresAtUtc: '2099-01-01T00:00:00Z',
  email: 'admin@crm.local',
  role: 'Admin',
};

describe('LoginComponent', () => {
  let signIn: ReturnType<typeof vi.fn>;
  let signedIn: ReturnType<typeof signal<boolean>>;
  let navigateByUrl: ReturnType<typeof vi.fn>;

  function setup(queryParams: Record<string, string> = {}) {
    signIn = vi.fn();
    signedIn = signal(false);
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { signIn, isSignedIn: signedIn } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } },
      ],
    });
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true) as never;
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    return fixture;
  }

  function fill(fixture: ComponentFixture<LoginComponent>, email: string, password: string) {
    const root = fixture.nativeElement as HTMLElement;
    const emailInput = root.querySelector('input[formControlName="email"]') as HTMLInputElement;
    const passwordInput = root.querySelector('input[formControlName="password"]') as HTMLInputElement;
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function submit(fixture: ComponentFixture<LoginComponent>) {
    const form = (fixture.nativeElement as HTMLElement).querySelector('form') as HTMLFormElement;
    form.dispatchEvent(new Event('submit'));
    // Material updates a field's error state during change detection, so a second pass shows the error.
    fixture.detectChanges();
    fixture.detectChanges();
  }

  const text = (fixture: ComponentFixture<LoginComponent>) => (fixture.nativeElement as HTMLElement).textContent ?? '';

  beforeEach(() => vi.restoreAllMocks());

  it('explains that the session expired when sent here with reason=expired', () => {
    const fixture = setup({ reason: 'expired' });

    expect(text(fixture)).toContain('Your session expired. Sign in again.');
  });

  it('shows no expiry message on a normal visit', () => {
    const fixture = setup();

    expect(text(fixture)).not.toContain('session expired');
  });

  it('shows required-field errors and sends nothing when the form is empty', () => {
    const fixture = setup();

    submit(fixture);

    expect(text(fixture)).toContain('Email is required.');
    expect(text(fixture)).toContain('Password is required.');
    expect(signIn).not.toHaveBeenCalled();
  });

  it('treats a whitespace-only email as empty and sends nothing', () => {
    const fixture = setup();

    fill(fixture, '   ', 'pw');
    submit(fixture);

    expect(text(fixture)).toContain('Email is required.');
    expect(signIn).not.toHaveBeenCalled();
  });

  it('sends the email without surrounding spaces', () => {
    const fixture = setup();
    signIn.mockReturnValue(of(session));

    fill(fixture, '  admin@crm.local ', 'pw');
    submit(fixture);

    expect(signIn).toHaveBeenCalledWith('admin@crm.local', 'pw');
  });

  it('shows the API problem text for a 400 or a server error instead of a generic message', () => {
    const fixture = setup();
    signIn.mockReturnValue(
      throwError(
        () => new HttpErrorResponse({ status: 400, error: { errors: { password: ['Password must be at most 128 characters.'] } } }),
      ),
    );

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="login-error"]');
    expect(alert?.textContent).toContain('Password must be at most 128 characters.');
  });

  it('falls back to a generic message when a failure carries no usable detail', () => {
    const fixture = setup();
    signIn.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 500 })));

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);

    expect(text(fixture)).toContain('Sign-in failed. Try again.');
  });

  it('ignores a second submit while the first is still in flight', () => {
    const fixture = setup();
    signIn.mockReturnValue(new Subject<Session>());

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);
    submit(fixture);

    expect(signIn).toHaveBeenCalledTimes(1);
  });

  it.each(['/\\evil.example', 'HTTP://evil.example'])('ignores the unsafe returnUrl %s', (returnUrl) => {
    const fixture = setup({ returnUrl });
    signIn.mockReturnValue(of(session));

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);

    expect(navigateByUrl).toHaveBeenCalledWith('/', { replaceUrl: true });
  });

  it('shows the generic message when the API refuses the credentials', () => {
    const fixture = setup();
    signIn.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));

    fill(fixture, 'admin@crm.local', 'wrong');
    submit(fixture);

    expect(text(fixture)).toContain('Email or password is incorrect.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('shows a different message when the service cannot be reached', () => {
    const fixture = setup();
    signIn.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);

    expect(text(fixture)).toContain('Cannot reach the service');
    expect(text(fixture)).not.toContain('Email or password is incorrect.');
  });

  it('disables the button while the request is in flight', () => {
    const fixture = setup();
    signIn.mockReturnValue(new Subject<Session>());

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);

    const button = (fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
  });

  it('goes to the home page after a successful sign-in', () => {
    const fixture = setup();
    signIn.mockReturnValue(of(session));

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);

    expect(signIn).toHaveBeenCalledWith('admin@crm.local', 'pw');
    expect(navigateByUrl).toHaveBeenCalledWith('/', { replaceUrl: true });
  });

  it('returns to the page the person originally asked for', () => {
    const fixture = setup({ returnUrl: '/admin' });
    signIn.mockReturnValue(of(session));

    fill(fixture, 'admin@crm.local', 'pw');
    submit(fixture);

    expect(navigateByUrl).toHaveBeenCalledWith('/admin', { replaceUrl: true });
  });

  it.each(['https://evil.example/', '//evil.example', 'javascript:alert(1)'])(
    'ignores an unsafe returnUrl (%s) and goes to the home page',
    (returnUrl) => {
      const fixture = setup({ returnUrl });
      signIn.mockReturnValue(of(session));

      fill(fixture, 'admin@crm.local', 'pw');
      submit(fixture);

      expect(navigateByUrl).toHaveBeenCalledWith('/', { replaceUrl: true });
    },
  );

  it('sends an already signed-in visitor to the home page', () => {
    signIn = vi.fn();
    signedIn = signal(true);
    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { signIn, isSignedIn: signedIn } },
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      ],
    });
    const spy = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    TestBed.createComponent(LoginComponent).detectChanges();

    expect(spy).toHaveBeenCalledWith('/', { replaceUrl: true });
  });
});
