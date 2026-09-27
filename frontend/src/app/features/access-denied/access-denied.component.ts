import { Component } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'app-access-denied',
  imports: [MatButtonModule, MatCardModule, RouterLink],
  template: `
    <mat-card>
      <mat-card-header>
        <h1 mat-card-title>Access denied</h1>
      </mat-card-header>
      <mat-card-content>
        <p>You do not have access to this area.</p>
      </mat-card-content>
      <mat-card-actions>
        <a mat-button routerLink="/">Back to home</a>
      </mat-card-actions>
    </mat-card>
  `,
})
export class AccessDeniedComponent {}
