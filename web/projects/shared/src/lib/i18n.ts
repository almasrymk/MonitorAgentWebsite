import { DOCUMENT } from '@angular/common';
import { computed, inject, Injectable, InjectionToken, Pipe, PipeTransform, signal } from '@angular/core';

export type Lang = 'ar' | 'en';
export const LANGS: readonly Lang[] = ['ar', 'en'];
export const DEFAULT_LANG: Lang = 'ar';

export function isLang(value: unknown): value is Lang {
  return value === 'ar' || value === 'en';
}

/** One app's texts: the same keys in both languages, so a missing translation is a compile error. */
export type Dictionary<K extends string = string> = Record<Lang, Record<K, string>>;

export const DICTIONARY = new InjectionToken<Dictionary>('DICTIONARY');

/** The current language. Sets lang and dir on the page, server-side too, so Arabic renders right-to-left from the first paint. */
@Injectable({ providedIn: 'root' })
export class I18n {
  private readonly document = inject(DOCUMENT);
  private readonly dictionary = inject(DICTIONARY);
  private readonly current = signal<Lang>(DEFAULT_LANG);

  readonly lang = this.current.asReadonly();
  readonly dir = computed(() => (this.current() === 'ar' ? 'rtl' : 'ltr'));
  readonly other = computed<Lang>(() => (this.current() === 'ar' ? 'en' : 'ar'));

  use(lang: Lang): void {
    this.current.set(lang);
    const html = this.document.documentElement;
    html.lang = lang;
    html.dir = lang === 'ar' ? 'rtl' : 'ltr';
  }

  /** The text for <paramref name="key"/>, with {name} placeholders filled from <paramref name="values"/>. */
  t(key: string, values?: Record<string, string | number>): string {
    let text = this.dictionary[this.current()][key] ?? key;
    if (values) for (const [name, value] of Object.entries(values)) text = text.replaceAll(`{${name}}`, String(value));
    return text;
  }
}

/** {{ 'nav.pricing' | t }} — re-evaluates when the language changes. */
@Pipe({ name: 't', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18n);

  transform(key: string, values?: Record<string, string | number>): string {
    return this.i18n.t(key, values);
  }
}
