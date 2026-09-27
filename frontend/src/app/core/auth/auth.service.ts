import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../../environments/environment';
import { Role, Session } from './auth.models';

export const SESSION_STORAGE_KEY = 'billingcrm.session';

/** 'active' = usable, 'expired' = there was one and it ran out (now cleared), 'none' = never signed in. */
export type SessionCheck = 'active' | 'expired' | 'none';

function isRole(value: unknown): value is Role {
  return value === 'Admin' || value === 'Agent';
}

function hasExpired(session: Session): boolean {
  const expiresAt = Date.parse(session.expiresAtUtc);
  // An unreadable expiry is treated as expired rather than trusted.
  return Number.isNaN(expiresAt) || expiresAt <= Date.now();
}

function isSession(value: unknown): value is Session {
  const candidate = value as Partial<Session> | null;
  return (
    typeof candidate?.accessToken === 'string' &&
    typeof candidate.expiresAtUtc === 'string' &&
    typeof candidate.email === 'string' &&
    isRole(candidate.role)
  );
}

/** Reads the stored session; anything missing, malformed or unreadable means "signed out". */
function readStoredSession(): Session | null {
  try {
    const raw = sessionStorage.getItem(SESSION_STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed: unknown = JSON.parse(raw);
    if (isSession(parsed)) {
      return parsed;
    }
  } catch {
    // Storage blocked or JSON corrupted: treat as signed out.
  }
  return null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly current = signal<Session | null>(readStoredSession());

  readonly session = this.current.asReadonly();
  readonly email = computed(() => this.current()?.email ?? null);
  readonly role = computed(() => this.current()?.role ?? null);

  /**
   * True only while a session exists and has not expired. A method, not a signal: expiry depends on
   * the clock, which a cached signal value would not follow.
   */
  isSignedIn(): boolean {
    const session = this.current();
    return session !== null && !hasExpired(session);
  }

  /** Evaluates the session against the clock. An expired session is cleared and reported as such. */
  checkSession(): SessionCheck {
    const session = this.current();
    if (session === null) {
      return 'none';
    }
    if (hasExpired(session)) {
      this.clear();
      return 'expired';
    }
    return 'active';
  }

  /** Signs in. The browser copy is for display and routing only; the API is the enforcement point. */
  signIn(email: string, password: string): Observable<Session> {
    return this.http
      .post<Session>(`${environment.apiBaseUrl}/api/auth/login`, { email, password })
      .pipe(
        tap((session) => {
          // Never trust the shape of a response blindly: an unexpected body must not create a session.
          if (!isSession(session)) {
            throw new Error('Unexpected sign-in response.');
          }
          this.store(session);
        }),
      );
  }

  /** Client-side only: the token is discarded. Nothing is revoked on the server (spec assumption). */
  signOut(): void {
    this.clear();
  }

  private store(session: Session): void {
    this.current.set(session);
    try {
      sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(session));
    } catch {
      // Storage unavailable: the person stays signed in for this page only.
    }
  }

  private clear(): void {
    this.current.set(null);
    try {
      sessionStorage.removeItem(SESSION_STORAGE_KEY);
    } catch {
      // Nothing more to do; the in-memory session is already gone.
    }
  }
}
