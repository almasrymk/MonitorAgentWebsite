import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { AppShell, Me, Session } from 'shared';

export const STAFF_ROLES = ['Admin', 'Support', 'Finance', 'ContentEditor', 'ContentReviewer'];

export function isStaff(me: Me | null): boolean {
  return !!me && me.roles.some((role) => STAFF_ROLES.includes(role));
}

/**
 * Staff only. The API also requires two-step verification for every admin call; a staff member without it is sent
 * to the account page to turn it on first.
 */
const staff: CanActivateFn = async (_route, state) => {
  const session = inject(Session);
  const router = inject(Router);
  const me = await session.load();
  if (!me) return router.createUrlTree(['/sign-in'], { queryParams: { returnUrl: state.url } });
  if (!isStaff(me)) return router.createUrlTree(['/no-access']);
  if (!me.mfaEnabled && !state.url.startsWith('/account')) return router.createUrlTree(['/account'], { queryParams: { setup: 'mfa' } });
  return true;
};

export const routes: Routes = [
  { path: 'sign-in', loadComponent: () => import('shared').then((m) => m.SignInPage) },
  { path: 'no-access', loadComponent: () => import('./pages/no-access').then((m) => m.NoAccessPage) },
  {
    path: '',
    component: AppShell,
    canActivateChild: [staff],
    data: {
      nav: [
        { path: '/', label: 'nav.overview' },
        { path: '/orders', label: 'nav.orders' },
        { path: '/releases', label: 'nav.releases' },
        { path: '/security', label: 'nav.security' },
        { path: '/account', label: 'nav.account' },
      ],
    },
    children: [
      { path: '', loadComponent: () => import('./pages/overview').then((m) => m.OverviewPage) },
      { path: 'orders', loadComponent: () => import('./pages/orders').then((m) => m.OrdersPage) },
      { path: 'releases', loadComponent: () => import('./pages/releases').then((m) => m.ReleasesPage) },
      { path: 'security', loadComponent: () => import('./pages/security').then((m) => m.SecurityPage) },
      { path: 'account', loadComponent: () => import('shared').then((m) => m.AccountPage) },
    ],
  },
  { path: '**', redirectTo: '' },
];
