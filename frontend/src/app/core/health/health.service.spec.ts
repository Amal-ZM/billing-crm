import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { HealthResponse, HealthService } from './health.service';

describe('HealthService', () => {
  const url = `${environment.apiBaseUrl}/health`;
  let service: HealthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(HealthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function collect() {
    const result: { value?: HealthResponse; error?: unknown } = {};
    service.getHealth().subscribe({
      next: (value) => (result.value = value),
      error: (error) => (result.error = error),
    });
    return result;
  }

  it('requests GET {apiBaseUrl}/health and returns the body on 200', () => {
    const result = collect();
    const request = http.expectOne(url);
    expect(request.request.method).toBe('GET');

    request.flush({ status: 'Healthy', checks: { database: 'Healthy' } });

    expect(result.value).toEqual({ status: 'Healthy', checks: { database: 'Healthy' } });
    expect(result.error).toBeUndefined();
  });

  it('treats a 503 with a health body as a normal (unhealthy) result', () => {
    const result = collect();

    http
      .expectOne(url)
      .flush({ status: 'Unhealthy', checks: { database: 'Unhealthy' } }, { status: 503, statusText: 'Service Unavailable' });

    expect(result.value?.status).toBe('Unhealthy');
    expect(result.error).toBeUndefined();
  });

  it('errors on a 503 whose body is not a health response', () => {
    const result = collect();

    http.expectOne(url).flush('<html>gateway down</html>', { status: 503, statusText: 'Service Unavailable' });

    expect(result.value).toBeUndefined();
    expect(result.error).toBeDefined();
  });

  it('errors on other HTTP failures', () => {
    const result = collect();

    http.expectOne(url).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(result.error).toBeDefined();
  });

  it('errors on a network failure (status 0, e.g. API down or CORS blocked)', () => {
    const result = collect();

    http.expectOne(url).error(new ProgressEvent('error'));

    expect(result.error).toBeDefined();
  });
});
