import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Api, I18n, PublicPage, RichText, TranslatePipe, WhenPipe } from 'shared';
import { useSeoText } from '../seo';
import { localized } from './content';

/**
 * Terms, privacy and refunds: route data says which. Editors can replace the built-in text by publishing a page with
 * the same address (terms, privacy, refunds) in the admin panel.
 */
@Component({
  selector: 'site-legal',
  imports: [RichText, TranslatePipe, WhenPipe],
  template: `
    <section class="section">
      <article class="container narrow card legal">
        @if (published(); as page) {
          <h1>{{ page.title }}</h1>
          <p class="muted">{{ 'legal.updated' | t: { date: (page.publishedAt | when) } }}</p>
          <ma-rich-text [text]="page.body" />
        } @else {
          <h1>{{ 'legal.' + page + '.title' | t }}</h1>
          <p class="muted">{{ 'legal.updated' | t: { date: (updated | when) } }}</p>
          <p>{{ 'legal.' + page + '.body' | t }}</p>
        }
      </article>
    </section>
  `,
})
export class LegalPage implements OnInit {
  protected readonly page: string = inject(ActivatedRoute).snapshot.data['page'];
  protected readonly updated = '2026-10-15T00:00:00Z';
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly cms = signal<PublicPage | null>(null);
  protected readonly published = computed(() => localized(this.cms(), this.i18n.lang()));

  constructor() {
    useSeoText(() => this.published() ?? { title: this.i18n.t(`legal.${this.page}.title`), description: this.i18n.t(`legal.${this.page}.body`) });
  }

  async ngOnInit(): Promise<void> {
    try {
      this.cms.set(await this.api.get<PublicPage>(`/api/v1/content/pages/${this.page}`));
    } catch {
      // Nothing published yet, or the API is down: the built-in text stands.
    }
  }
}
