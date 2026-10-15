import { effect, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';
import { I18n } from 'shared';

/** Sets the page title and description in the current language, also when rendered on the server. */
export function useSeo(titleKey: string, descriptionKey: string): void {
  const i18n = inject(I18n);
  const title = inject(Title);
  const meta = inject(Meta);
  effect(() => {
    i18n.lang();
    title.setTitle(`${i18n.t(titleKey)} | ${i18n.t('brand')}`);
    meta.updateTag({ name: 'description', content: i18n.t(descriptionKey) });
  });
}

/** Like useSeo, for text that comes from the content API instead of the translation tables. */
export function useSeoText(text: () => { title: string; description: string } | null): void {
  const i18n = inject(I18n);
  const title = inject(Title);
  const meta = inject(Meta);
  effect(() => {
    const current = text();
    if (!current) return;
    title.setTitle(`${current.title} | ${i18n.t('brand')}`);
    meta.updateTag({ name: 'description', content: current.description || current.title });
  });
}
