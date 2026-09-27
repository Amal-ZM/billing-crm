import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { describe, expect, it } from 'vitest';
import { AccessDeniedComponent } from './access-denied.component';

describe('AccessDeniedComponent', () => {
  it('tells the person they do not have access, offers a way home, and shows no other content', async () => {
    await TestBed.configureTestingModule({
      imports: [AccessDeniedComponent],
      providers: [provideRouter([])],
    }).compileComponents();
    const fixture = TestBed.createComponent(AccessDeniedComponent);
    await fixture.whenStable();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('You do not have access to this area.');
    expect(element.querySelector('a[href="/"]')?.textContent).toContain('Back to home');
    expect(element.textContent).not.toContain('Administration');
  });
});
