import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n, TranslatePipe } from 'shared';
import { useSeo } from '../seo';

@Component({
  selector: 'site-home',
  imports: [RouterLink, TranslatePipe],
  template: `
    <section class="hero">
      <div class="container hero-grid">
        <div>
          <p class="eyebrow">{{ 'home.eyebrow' | t }}</p>
          <h1>{{ 'home.title' | t }}</h1>
          <p class="lead">{{ 'home.lead' | t }}</p>
          <div class="cta-row">
            <a class="btn" [routerLink]="['/', i18n.lang(), 'pricing']">{{ 'home.ctaPrimary' | t }}</a>
            <a class="btn secondary" [routerLink]="['/', i18n.lang(), 'download']">{{ 'home.ctaSecondary' | t }}</a>
          </div>
          <ul class="checks">
            <li>{{ 'home.point1' | t }}</li>
            <li>{{ 'home.point2' | t }}</li>
            <li>{{ 'home.point3' | t }}</li>
          </ul>
        </div>
        <div class="card preview" aria-hidden="true">
          <div class="preview-head"><span></span><span></span><span></span></div>
          @for (row of previewRows; track row.name) {
            <div class="preview-row">
              <span class="mono">{{ row.name }}</span>
              <span class="badge" [class.ok]="row.state === 'ok'" [class.warn]="row.state === 'warn'" [class.bad]="row.state === 'bad'">{{ row.label }}</span>
            </div>
          }
        </div>
      </div>
    </section>

    <section class="section" aria-labelledby="features-title">
      <div class="container">
        <h2 id="features-title">{{ 'home.featuresTitle' | t }}</h2>
        <div class="grid-3">
          @for (feature of features; track feature) {
            <article class="card">
              <h3>{{ 'feature.' + feature + '.title' | t }}</h3>
              <p class="muted">{{ 'feature.' + feature + '.body' | t }}</p>
            </article>
          }
        </div>
      </div>
    </section>

    <section class="section alt" aria-labelledby="steps-title">
      <div class="container">
        <h2 id="steps-title">{{ 'home.stepsTitle' | t }}</h2>
        <ol class="steps">
          @for (step of [1, 2, 3]; track step) {
            <li class="card">
              <span class="step-number" aria-hidden="true">{{ step }}</span>
              <h3>{{ 'step.' + step + '.title' | t }}</h3>
              <p class="muted">{{ 'step.' + step + '.body' | t }}</p>
            </li>
          }
        </ol>
      </div>
    </section>

    <section class="section" aria-labelledby="faq-title">
      <div class="container narrow">
        <h2 id="faq-title">{{ 'home.faqTitle' | t }}</h2>
        @for (q of [1, 2, 3, 4]; track q) {
          <details class="faq">
            <summary>{{ 'faq.' + q + '.q' | t }}</summary>
            <p>{{ 'faq.' + q + '.a' | t }}</p>
          </details>
        }
      </div>
    </section>

    <section class="section">
      <div class="container card final-cta">
        <div>
          <h2>{{ 'home.finalTitle' | t }}</h2>
          <p class="muted">{{ 'home.finalBody' | t }}</p>
        </div>
        <a class="btn" [routerLink]="['/', i18n.lang(), 'pricing']">{{ 'home.ctaPrimary' | t }}</a>
      </div>
    </section>
  `,
})
export class HomePage {
  protected readonly i18n = inject(I18n);
  protected readonly features = ['devices', 'services', 'alerts', 'reports', 'security', 'deploy'];
  // An illustration of the product, not real customer data.
  protected readonly previewRows = [
    { name: 'SRV-DC-01', label: 'Online', state: 'ok' },
    { name: 'SRV-FILE-01', label: 'Online', state: 'ok' },
    { name: 'SRV-APP-01', label: 'Warning', state: 'warn' },
    { name: 'PC-OFFICE-12', label: 'Online', state: 'ok' },
    { name: 'PC-OFFICE-23', label: 'Offline', state: 'bad' },
  ];

  constructor() {
    useSeo('home.title', 'home.lead');
  }
}
