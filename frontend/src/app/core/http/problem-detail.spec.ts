import { HttpErrorResponse } from '@angular/common/http';
import { describe, expect, it } from 'vitest';
import { problemDetail } from './problem-details';

describe('problemDetail', () => {
  it('returns the first field message of a 400', () => {
    const error = new HttpErrorResponse({ status: 400, error: { errors: { pageSize: ['Page size must be 1 to 100.'], page: ['Bad page.'] } } });

    expect(problemDetail(error)).toBe('Page size must be 1 to 100.');
  });

  it('returns the ProblemDetails detail otherwise', () => {
    expect(problemDetail(new HttpErrorResponse({ status: 500, error: { detail: 'Boom.' } }))).toBe('Boom.');
  });

  it('returns null when the API said nothing usable', () => {
    expect(problemDetail(new HttpErrorResponse({ status: 500 }))).toBeNull();
    expect(problemDetail(new HttpErrorResponse({ status: 500, error: '<html></html>' }))).toBeNull();
    expect(problemDetail(new HttpErrorResponse({ status: 400, error: { errors: { x: [42] } } }))).toBeNull();
    expect(problemDetail(new Error('plain'))).toBeNull();
  });
});
