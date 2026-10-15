import { Component, computed, inject, input, RESPONSE_INIT } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { I18n, RichText, TranslatePipe, WhenPipe } from 'shared';
import { useSeoText } from '../seo';
import { Loaded, localized } from './content-data';

/** Any page editors publish from the admin panel, at /{lang}/pages/{slug}. */
@Component({
  selector: 'site-content',
  imports: [RichText, RouterLink, TranslatePipe, WhenPipe],
  template: `
    <section class="section">
      <article class="container narrow card legal">
        @switch (page().kind) {
          @case ('ready') {
            @if (text(); as page) {
              <h1>{{ page.title }}</h1>
              <p class="muted">{{ 'legal.updated' | t: { date: (page.publishedAt | when) } }}</p>
              <ma-rich-text [text]="page.body" />
            }
          }
          @case ('error') {
            <h1>{{ 'content.errorTitle' | t }}</h1>
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
  /** From the route resolver. */
  readonly page = input.required<Loaded>();
  protected readonly i18n = inject(I18n);
  protected readonly text = computed(() => {
    const loaded = this.page();
    return localized(loaded.kind === 'ready' ? loaded.page : null, this.i18n.lang());
  });

  constructor() {
    // Search engines and caches must see a missing page as missing, and an outage as temporary.
    const response = inject(RESPONSE_INIT, { optional: true });
    const resolved: Loaded = inject(ActivatedRoute).snapshot.data['page'];
    if (response && resolved.kind !== 'ready') response.status = resolved.kind === 'missing' ? 404 : 503;
    useSeoText(() => {
      const kind = this.page().kind;
      if (kind === 'missing') return { title: this.i18n.t('notFound.title'), description: this.i18n.t('notFound.body') };
      if (kind === 'error') return { title: this.i18n.t('content.errorTitle'), description: this.i18n.t('content.error') };
      return this.text();
    });
  }
}
