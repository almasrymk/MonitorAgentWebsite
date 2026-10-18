import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { Session } from 'shared';
import { Layout } from './layout';

/** Signed-in pages: anyone else goes to sign-in and comes back afterwards. */
const signedIn: CanActivateFn = async (_route, state) => {
  // Both injected before the await: inject() only works synchronously.
  const session = inject(Session);
  const router = inject(Router);
  const me = await session.load();
  return me ? true : router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
};

export const routes: Routes = [
  { path: 'sign-in', loadComponent: () => import('./pages/auth').then((m) => m.SignInPage) },
  { path: 'register', loadComponent: () => import('./pages/auth').then((m) => m.RegisterPage) },
  { path: 'forgot-password', loadComponent: () => import('./pages/auth').then((m) => m.ForgotPasswordPage) },
  // Links from emails (see the API's PortalLinks).
  { path: 'auth/confirm-email', loadComponent: () => import('./pages/auth').then((m) => m.ConfirmEmailPage) },
  { path: 'auth/reset-password', loadComponent: () => import('./pages/auth').then((m) => m.ResetPasswordPage) },
  {
    path: '',
    component: Layout,
    canActivate: [signedIn],
    children: [
      { path: '', loadComponent: () => import('./pages/overview').then((m) => m.OverviewPage) },
      { path: 'licenses', loadComponent: () => import('./pages/licenses').then((m) => m.LicensesPage) },
      { path: 'licenses/:id', loadComponent: () => import('./pages/licenses').then((m) => m.LicensePage) },
      { path: 'checkout', loadComponent: () => import('./pages/orders').then((m) => m.CheckoutPage) },
      { path: 'checkout/sandbox', loadComponent: () => import('./pages/orders').then((m) => m.SandboxCheckoutPage) },
      { path: 'orders', loadComponent: () => import('./pages/orders').then((m) => m.OrdersPage) },
      { path: 'orders/:id', loadComponent: () => import('./pages/orders').then((m) => m.OrderPage) },
      { path: 'invoices', loadComponent: () => import('./pages/invoices').then((m) => m.InvoicesPage) },
      { path: 'invoices/:id', loadComponent: () => import('./pages/invoices').then((m) => m.InvoicePage) },
      { path: 'company', loadComponent: () => import('./pages/company').then((m) => m.CompanyPage) },
      { path: 'invitations/accept', loadComponent: () => import('./pages/company').then((m) => m.AcceptInvitationPage) },
      { path: 'account', loadComponent: () => import('./pages/account').then((m) => m.AccountPage) },
    ],
  },
  { path: '**', redirectTo: '' },
];
