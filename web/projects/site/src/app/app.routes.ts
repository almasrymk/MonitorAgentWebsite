import { Routes, UrlSegment } from '@angular/router';
import { isLang } from 'shared';
import { Shell } from './shell';

/** /ar/... and /en/... so each language has its own indexable address. */
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'ar' },
  {
    path: ':lang',
    component: Shell,
    canMatch: [(_route: unknown, segments: UrlSegment[]) => isLang(segments[0]?.path)],
    children: [
      { path: '', loadComponent: () => import('./pages/home').then((m) => m.HomePage) },
      { path: 'pricing', loadComponent: () => import('./pages/pricing').then((m) => m.PricingPage) },
      { path: 'download', loadComponent: () => import('./pages/download').then((m) => m.DownloadPage) },
      { path: 'terms', data: { page: 'terms' }, loadComponent: () => import('./pages/legal').then((m) => m.LegalPage) },
      { path: 'privacy', data: { page: 'privacy' }, loadComponent: () => import('./pages/legal').then((m) => m.LegalPage) },
      { path: 'refunds', data: { page: 'refunds' }, loadComponent: () => import('./pages/legal').then((m) => m.LegalPage) },
      { path: 'pages/:slug', loadComponent: () => import('./pages/content').then((m) => m.ContentPage) },
      { path: '**', loadComponent: () => import('./pages/not-found').then((m) => m.NotFoundPage) },
    ],
  },
  // An address without a language (an old link, a typo) goes to the Arabic page with the same path.
  { path: '**', redirectTo: ({ url }) => `/ar/${url.map((s) => s.path).join('/')}` },
];
