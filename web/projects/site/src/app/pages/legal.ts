import { Component, computed, inject, input } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { I18n, RichText, TranslatePipe, WhenPipe } from 'shared';
import { useSeoText } from '../seo';
import { Loaded, localized } from './content-data';

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
          <h1>{{ 'legal.' + name + '.title' | t }}</h1>
          <p class="muted">{{ 'legal.updated' | t: { date: (updated | when) } }}</p>
          <p>{{ 'legal.' + name + '.body' | t }}</p>
        }
      </article>
    </section>
  `,
})
export class LegalPage {
  /** From the route resolver: the published version, or nothing yet. */
  readonly cms = input.required<Loaded>();
  protected readonly name: string = inject(ActivatedRoute).snapshot.data['page'];
  protected readonly updated = '2026-10-15T00:00:00Z';
  private readonly i18n = inject(I18n);
  protected readonly published = computed(() => {
    const cms = this.cms();
    return localized(cms.kind === 'ready' ? cms.page : null, this.i18n.lang());
  });

  constructor() {
    useSeoText(() => this.published() ?? { title: this.i18n.t(`legal.${this.name}.title`), description: this.i18n.t(`legal.${this.name}.body`) });
  }
}
