import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n, TranslatePipe } from 'shared';
import { useSeo } from '../seo';

@Component({
  selector: 'site-not-found',
  imports: [RouterLink, TranslatePipe],
  template: `
    <section class="section">
      <div class="container narrow card">
        <h1>{{ 'notFound.title' | t }}</h1>
        <p>{{ 'notFound.body' | t }}</p>
        <a class="btn" [routerLink]="['/', i18n.lang()]">{{ 'notFound.home' | t }}</a>
      </div>
    </section>
  `,
})
export class NotFoundPage {
  protected readonly i18n = inject(I18n);

  constructor() {
    useSeo('notFound.title', 'notFound.body');
  }
}
