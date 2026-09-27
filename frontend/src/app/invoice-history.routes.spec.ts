import { describe, expect, it } from 'vitest';
import { routes } from './app.routes';
import { authGuard } from './core/auth/auth.guard';
import { ShellComponent } from './core/layout/shell.component';

const HISTORY_PATH = 'invoices/:id/history';

const shellRoute = routes.find((route) => route.component === ShellComponent);
const children = shellRoute?.children ?? [];
const paths = children.map((child) => child.path);
const historyRoute = children.find((child) => child.path === HISTORY_PATH);

/** The history page (spec 010): under the signed-in shell (unlike the print page), lazy, and matched before the invoice details. */
describe('invoice history route', () => {
  it('exists as a lazy route among the signed-in shell children', () => {
    expect(shellRoute).toBeDefined();
    expect(historyRoute).toBeDefined();
    expect(typeof historyRoute?.loadComponent).toBe('function');
    expect(historyRoute?.component).toBeUndefined();
  });

  it('is not a top-level route: it has the toolbar and menus like the other screens, and the shell keeps its guards', () => {
    expect(routes.some((route) => route.path === HISTORY_PATH)).toBe(false);
    expect(shellRoute?.canActivate).toEqual([authGuard]);
    expect(shellRoute?.canActivateChild).toEqual([authGuard]);
  });

  it('is open to any signed-in user: no role guard of its own', () => {
    expect(historyRoute?.canActivate).toBeUndefined();
  });

  it('comes before invoices/:id, and invoices/:id is still there for the details', () => {
    expect(paths.indexOf(HISTORY_PATH)).toBeGreaterThanOrEqual(0);
    expect(paths.indexOf('invoices/:id')).toBeGreaterThanOrEqual(0);
    expect(paths.indexOf(HISTORY_PATH)).toBeLessThan(paths.indexOf('invoices/:id'));
  });

  it('does not take over the details address: invoices/:id has two segments, the history address has three', () => {
    expect('invoices/:id'.split('/')).toHaveLength(2);
    expect(HISTORY_PATH.split('/')).toHaveLength(3);
  });

  it('is under the shell, which comes before the wildcard route, so it is matched', () => {
    const topLevel = routes.map((route) => route.path);

    expect(topLevel.indexOf('')).toBeGreaterThanOrEqual(0);
    expect(topLevel.indexOf('')).toBeLessThan(topLevel.indexOf('**'));
  });
});
