import { Component, computed, effect, inject, input, RESPONSE_INIT, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, ApiError, I18n, PublicPage, RichText, TranslatePipe, WhenPipe } from 'shared';
import { useSeoText } from '../seo';

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

type State = { kind: 'loading' } | { kind: 'missing' } | { kind: 'error' } | { kind: 'ready'; page: PublicPage };

/** Any page editors publish from the admin panel, at /{lang}/pages/{slug}. */
@Component({
  selector: 'site-content',
  imports: [RichText, RouterLink, TranslatePipe, WhenPipe],
  template: `
    <section class="section">
      <article class="container narrow card legal">
        @switch (state().kind) {
          @case ('loading') {
            <div aria-busy="true"><div class="skeleton" style="height: 10rem"></div></div>
          }
          @case ('ready') {
            @if (text(); as page) {
              <h1>{{ page.title }}</h1>
              <p class="muted">{{ 'legal.updated' | t: { date: (page.publishedAt | when) } }}</p>
              <ma-rich-text [text]="page.body" />
            }
          }
          @case ('error') {
            <div class="alert" role="alert">{{ 'content.error' | t }}</div>
          }
          @default {
            <h1>{{ 'notFound.title' | t }}</h1>
            <p>{{ 'notFound.body' | t }}</p>
            <a class="btn" [routerLink]="['/', i18n.lang()]">{{ 'notFound.home' | t }}</a>
          }
        }
      </article>
    </section>
  `,
})
export class ContentPage {
  readonly slug = input.required<string>();
  private readonly api = inject(Api);
  private readonly response = inject(RESPONSE_INIT, { optional: true });
  protected readonly i18n = inject(I18n);
  protected readonly state = signal<State>({ kind: 'loading' });
  protected readonly text = computed(() => {
    const state = this.state();
    return localized(state.kind === 'ready' ? state.page : null, this.i18n.lang());
  });

  constructor() {
    useSeoText(() => {
      const kind = this.state().kind;
      if (kind === 'missing') return { title: this.i18n.t('notFound.title'), description: this.i18n.t('notFound.body') };
      return this.text();
    });
    effect(() => {
      const slug = this.slug();
      untracked(() => void this.load(slug));
    });
  }

  private async load(slug: string): Promise<void> {
    this.state.set({ kind: 'loading' });
    try {
      this.state.set({ kind: 'ready', page: await this.api.get<PublicPage>(`/api/v1/content/pages/${encodeURIComponent(slug)}`) });
    } catch (error) {
      const missing = ApiError.from(error).status === 404;
      this.state.set({ kind: missing ? 'missing' : 'error' });
      if (this.response) this.response.status = missing ? 404 : 503;
    }
  }
}
