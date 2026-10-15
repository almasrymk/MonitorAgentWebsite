import { inject } from '@angular/core';
import { ResolveFn } from '@angular/router';
import { Api, ApiError, PublicPage } from 'shared';

/** The published page in the visitor's language, or null when there is none. */
export function localized(page: PublicPage | null, lang: string) {
  if (!page) return null;
  const ar = lang === 'ar';
  return {
    title: ar ? page.titleAr : page.titleEn,
    description: ar ? page.descriptionAr : page.descriptionEn,
    body: ar ? page.bodyAr : page.bodyEn,
    publishedAt: page.publishedAt,
  };
}

export type Loaded = { kind: 'ready'; page: PublicPage } | { kind: 'missing' } | { kind: 'error' };

/**
 * Loads a published page before its route shows, so the server renders it complete and the browser hydrates the same
 * markup instead of flashing a placeholder (no layout shift). Kept apart from the page components so the routes can
 * use it without pulling those components into the first download.
 */
export async function loadPage(api: Api, slug: string): Promise<Loaded> {
  try {
    return { kind: 'ready', page: await api.get<PublicPage>(`/api/v1/content/pages/${encodeURIComponent(slug)}`) };
  } catch (error) {
    return { kind: ApiError.from(error).status === 404 ? 'missing' : 'error' };
  }
}

export const contentResolver: ResolveFn<Loaded> = (route) => loadPage(inject(Api), route.paramMap.get('slug') ?? '');

/** For the legal pages; a failure means the built-in text is shown. */
export const legalResolver: ResolveFn<Loaded> = (route) => loadPage(inject(Api), route.data['page']);
