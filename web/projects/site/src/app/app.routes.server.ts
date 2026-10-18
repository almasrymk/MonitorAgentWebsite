import { RenderMode, ServerRoute } from '@angular/ssr';
import { LANGS } from 'shared';

const langs = async () => LANGS.map((lang) => ({ lang }));

/**
 * Pages whose text never changes are built ahead of time. Prices and versions come from the API, so those pages are
 * rendered per request and are always current for visitors and search engines.
 */
export const serverRoutes: ServerRoute[] = [
  { path: '', renderMode: RenderMode.Server },
  { path: ':lang', renderMode: RenderMode.Prerender, getPrerenderParams: langs },
  { path: ':lang/pricing', renderMode: RenderMode.Server },
  { path: ':lang/download', renderMode: RenderMode.Server },
  { path: ':lang/terms', renderMode: RenderMode.Prerender, getPrerenderParams: langs },
  { path: ':lang/privacy', renderMode: RenderMode.Prerender, getPrerenderParams: langs },
  { path: ':lang/refunds', renderMode: RenderMode.Prerender, getPrerenderParams: langs },
  { path: ':lang/**', renderMode: RenderMode.Server, status: 404 },
  { path: '**', renderMode: RenderMode.Server },
];
