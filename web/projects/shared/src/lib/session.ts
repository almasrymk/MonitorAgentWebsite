import { inject, Injectable, signal } from '@angular/core';
import { Api, ApiError } from './api';
import { DEFAULT_LANG, I18n, isLang, Lang } from './i18n';
import { Me } from './models';

/** Who is signed in, for the portal and admin. The session itself is an HttpOnly cookie the apps never see. */
@Injectable({ providedIn: 'root' })
export class Session {
  private readonly api = inject(Api);
  private readonly current = signal<Me | null>(null);
  private loaded = false;

  readonly me = this.current.asReadonly();

  /** Gets a CSRF token for this browser. Needed before the first POST, and again after signing in or out. */
  async refreshCsrf(): Promise<void> {
    await this.api.get('/api/v1/auth/csrf');
  }

  /** The signed-in user, or null. Asks the API once and then remembers. */
  async load(force = false): Promise<Me | null> {
    if (this.loaded && !force) return this.current();
    try {
      this.current.set(await this.api.get<Me>('/api/v1/account/me'));
    } catch (error) {
      if (!(error instanceof ApiError) || (error.status !== 401 && error.status !== 403)) throw error;
      this.current.set(null);
    }
    this.loaded = true;
    return this.current();
  }

  async signedIn(): Promise<void> {
    await this.refreshCsrf();
    await this.load(true);
  }

  async signOut(): Promise<void> {
    try {
      await this.api.post('/api/v1/auth/logout');
    } finally {
      this.current.set(null);
      this.loaded = true;
      await this.refreshCsrf().catch(() => undefined);
    }
  }
}

const LANG_KEY = 'ma.lang';

/** The portal and admin keep the chosen language in this browser; Arabic until one is chosen. */
export function startLanguage(i18n: I18n): void {
  let saved: string | null = null;
  try {
    saved = localStorage.getItem(LANG_KEY);
  } catch {
    // Storage can be blocked; the default language still works.
  }
  i18n.use(isLang(saved) ? saved : DEFAULT_LANG);
}

export function saveLanguage(i18n: I18n, lang: Lang): void {
  i18n.use(lang);
  try {
    localStorage.setItem(LANG_KEY, lang);
  } catch {
    // Not saved; the choice still applies until the page is closed.
  }
}

/** A message for an API failure: the text for its error code when the app has one, otherwise a general one. */
export function errorText(i18n: I18n, error: unknown): string {
  const code = error instanceof ApiError ? error.code : 'UNKNOWN';
  const key = `error.${code}`;
  const text = i18n.t(key);
  return text === key ? i18n.t('error.generic') : text;
}
