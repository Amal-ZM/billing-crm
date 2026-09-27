import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { Subject, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { AdminService, AdminStatus } from '../../core/admin/admin.service';
import { AdminComponent } from './admin.component';

function setup(getStatus: () => unknown) {
  TestBed.configureTestingModule({
    imports: [AdminComponent],
    providers: [{ provide: AdminService, useValue: { getStatus } }],
  });
  const fixture = TestBed.createComponent(AdminComponent);
  return { fixture, element: fixture.nativeElement as HTMLElement };
}

describe('AdminComponent', () => {
  it('shows the Administration placeholder and the status the API reports', () => {
    const response = new Subject<AdminStatus>();
    const { fixture, element } = setup(() => response);

    fixture.detectChanges();
    expect(element.textContent).toContain('Administration');
    expect(element.textContent).toContain('Checking');

    response.next({ area: 'admin', status: 'ok' });
    response.complete();
    fixture.detectChanges();
    expect(element.querySelector('[data-testid="admin-status"]')?.textContent).toContain('ok');
  });

  it('shows an error state when the API refuses or fails', () => {
    const { fixture, element } = setup(() => throwError(() => new HttpErrorResponse({ status: 403 })));

    fixture.detectChanges();

    expect(element.querySelector('[data-testid="admin-status"]')?.textContent).toContain('HTTP 403');
  });
});
