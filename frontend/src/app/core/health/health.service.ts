import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, catchError, of, throwError, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface HealthResponse {
  status: string;
  checks: Record<string, string>;
}

const REQUEST_TIMEOUT_MS = 10_000;

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);

  /**
   * Calls GET /health. A 503 from the API still carries a health body (database unhealthy),
   * so it is returned as a normal result; anything else (network down, CORS block, timeout,
   * other HTTP errors) is an error.
   */
  getHealth(): Observable<HealthResponse> {
    return this.http.get<HealthResponse>(`${environment.apiBaseUrl}/health`).pipe(
      timeout(REQUEST_TIMEOUT_MS),
      catchError((error: unknown) =>
        error instanceof HttpErrorResponse && error.status === 503 && error.error?.status
          ? of(error.error as HealthResponse)
          : throwError(() => error),
      ),
    );
  }
}
