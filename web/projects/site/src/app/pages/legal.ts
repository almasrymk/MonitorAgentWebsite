import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { TranslatePipe } from 'shared';
import { useSeo } from '../seo';

/** Terms, privacy and refunds: route data says which. */
@Component({
  selector: 'site-legal',
  imports: [TranslatePipe],
  template: `
    <section class="section">
      <article class="container narrow card legal">
        <h1>{{ 'legal.' + page + '.title' | t }}</h1>
        <p class="muted">{{ 'legal.updated' | t: { date: updated } }}</p>
        <p>{{ 'legal.' + page + '.body' | t }}</p>
      </article>
    </section>
  `,
})
export class LegalPage {
  protected readonly page: string = inject(ActivatedRoute).snapshot.data['page'];
  protected readonly updated = '2026-10-15';

  constructor() {
    useSeo(`legal.${this.page}.title`, `legal.${this.page}.body`);
  }
}
