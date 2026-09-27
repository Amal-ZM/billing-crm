import { Routes } from '@angular/router';
import { authGuard, roleGuard } from './core/auth/auth.guard';
import { ShellComponent } from './core/layout/shell.component';
import { AccessDeniedComponent } from './features/access-denied/access-denied.component';
import { AdminComponent } from './features/admin/admin.component';
import { HomeComponent } from './features/home/home.component';
import { LoginComponent } from './features/login/login.component';

export const routes: Routes = [
  { path: 'login', component: LoginComponent },
  // The printable invoice (spec 009) is deliberately NOT under the shell: nothing of the application's chrome (toolbar, menus, Sign out)
  // may be part of the page, so there is nothing to hide when it is printed. It is behind the same guard as every protected screen.
  {
    path: 'invoices/:id/print',
    canActivate: [authGuard],
    loadComponent: () => import('./features/invoices/invoice-print.component').then((m) => m.InvoicePrintComponent),
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    // canActivate runs only when the shell first activates; canActivateChild re-checks expiry on every
    // navigation between its children (spec FR-010).
    canActivateChild: [authGuard],
    children: [
      { path: '', component: HomeComponent },
      {
        path: 'customers',
        loadComponent: () => import('./features/customers/customer-list.component').then((m) => m.CustomerListComponent),
      },
      {
        path: 'customers/new',
        loadComponent: () => import('./features/customers/customer-form.component').then((m) => m.CustomerFormComponent),
      },
      {
        path: 'customers/:id/edit',
        loadComponent: () => import('./features/customers/customer-form.component').then((m) => m.CustomerFormComponent),
      },
      {
        path: 'customers/:id',
        loadComponent: () => import('./features/customers/customer-detail.component').then((m) => m.CustomerDetailComponent),
      },
      // Products: anyone signed in may read; adding and editing are Admin only (the API refuses an Agent with 403 as well).
      {
        path: 'products',
        loadComponent: () => import('./features/products/product-list.component').then((m) => m.ProductListComponent),
      },
      {
        path: 'products/new',
        canActivate: [roleGuard('Admin')],
        loadComponent: () => import('./features/products/product-form.component').then((m) => m.ProductFormComponent),
      },
      {
        path: 'products/:id/edit',
        canActivate: [roleGuard('Admin')],
        loadComponent: () => import('./features/products/product-form.component').then((m) => m.ProductFormComponent),
      },
      {
        path: 'products/:id',
        loadComponent: () => import('./features/products/product-detail.component').then((m) => m.ProductDetailComponent),
      },
      // Invoices: any signed-in staff member. `new` is listed before `:id` so it is not read as an id.
      {
        path: 'invoices',
        loadComponent: () => import('./features/invoices/invoice-list.component').then((m) => m.InvoiceListComponent),
      },
      {
        path: 'invoices/new',
        loadComponent: () => import('./features/invoices/invoice-form.component').then((m) => m.InvoiceFormComponent),
      },
      {
        path: 'invoices/:id/edit',
        loadComponent: () => import('./features/invoices/invoice-form.component').then((m) => m.InvoiceFormComponent),
      },
      {
        path: 'invoices/:id/payments/new',
        loadComponent: () => import('./features/invoices/payment-form.component').then((m) => m.PaymentFormComponent),
      },
      // The history of an invoice (spec 010): any signed-in user, under the shell like the details (unlike the print page).
      {
        path: 'invoices/:id/history',
        loadComponent: () => import('./features/invoices/invoice-history.component').then((m) => m.InvoiceHistoryComponent),
      },
      {
        path: 'invoices/:id',
        loadComponent: () => import('./features/invoices/invoice-detail.component').then((m) => m.InvoiceDetailComponent),
      },
      { path: 'admin', component: AdminComponent, canActivate: [roleGuard('Admin')] },
      { path: 'access-denied', component: AccessDeniedComponent },
    ],
  },
  { path: '**', redirectTo: '' },
];
