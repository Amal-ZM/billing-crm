import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface AdminStatus {
  area: string;
  status: string;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  /** Admin-only on the API: an Agent's token gets 403 even if this method is called. */
  getStatus(): Observable<AdminStatus> {
    return this.http.get<AdminStatus>(`${environment.apiBaseUrl}/api/admin/status`);
  }
}
