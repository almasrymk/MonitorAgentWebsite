import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n, TranslatePipe } from 'shared';
import { useSeo } from '../seo';

/** The home page follows the customer guide (Monitor Agent - دليل العميل): promise, problem, product, proof. */
@Component({
  selector: 'site-home',
  imports: [RouterLink, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <section class="hero">
      <div class="container hero-grid">
        <div>
          <p class="eyebrow">{{ 'home.eyebrow' | t }}</p>
          <h1>{{ 'home.title' | t }}</h1>
          <p class="lead">{{ 'home.lead' | t }}</p>
          <div class="cta-row">
            <a class="btn" [routerLink]="['/', i18n.lang(), 'pricing']">{{
              'home.ctaPrimary' | t
            }}</a>
            <a class="btn secondary" [routerLink]="['/', i18n.lang(), 'download']">{{
              'home.ctaSecondary' | t
            }}</a>
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
              <span
                class="badge"
                [class.ok]="row.state === 'ok'"
                [class.warn]="row.state === 'warn'"
                [class.bad]="row.state === 'bad'"
                >{{ row.label }}</span
              >
            </div>
          }
        </div>
      </div>
    </section>

    <section class="section" aria-labelledby="promises-title">
      <div class="container">
        <h2 id="promises-title">{{ 'home.promisesTitle' | t }}</h2>
        <p class="lead">{{ 'home.promisesLead' | t }}</p>
        <div class="grid-3">
          @for (promise of ['know', 'understand', 'proof']; track promise) {
            <article class="card">
              <h3>{{ 'promise.' + promise + '.title' | t }}</h3>
              <p class="muted">{{ 'promise.' + promise + '.body' | t }}</p>
            </article>
          }
        </div>
        <p class="quote">{{ 'home.quote' | t }}</p>
      </div>
    </section>

    <section class="section alt" aria-labelledby="challenge-title">
      <div class="container">
        <h2 id="challenge-title">{{ 'home.challengeTitle' | t }}</h2>
        <p class="lead">{{ 'home.challengeLead' | t }}</p>
        <div class="table-wrap" tabindex="0" role="region" aria-labelledby="challenge-title">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'challenge.colSituation' | t }}</th>
                <th scope="col">{{ 'challenge.colWhat' | t }}</th>
                <th scope="col">{{ 'challenge.colResult' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of [1, 2, 3, 4, 5, 6]; track row) {
                <tr>
                  <th scope="row">{{ 'challenge.' + row + '.situation' | t }}</th>
                  <td>{{ 'challenge.' + row + '.what' | t }}</td>
                  <td>{{ 'challenge.' + row + '.result' | t }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <section class="section" id="features" aria-labelledby="features-title">
      <div class="container">
        <h2 id="features-title">{{ 'home.featuresTitle' | t }}</h2>
        <p class="lead">{{ 'home.featuresLead' | t }}</p>
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

    <section class="section alt" aria-labelledby="alerts-title">
      <div class="container">
        <h2 id="alerts-title">{{ 'home.alertsTitle' | t }}</h2>
        <p class="lead">{{ 'home.alertsLead' | t }}</p>
        <div class="grid-3">
          @for (state of ['ok', 'warn', 'bad']; track state) {
            <article class="card">
              <span
                class="badge"
                [class.ok]="state === 'ok'"
                [class.warn]="state === 'warn'"
                [class.bad]="state === 'bad'"
                >{{ 'state.' + state + '.title' | t }}</span
              >
              <p class="muted">{{ 'state.' + state + '.body' | t }}</p>
            </article>
          }
        </div>
        <p>{{ 'home.board' | t }}</p>
        <h3 class="section-title">{{ 'home.lifecycleTitle' | t }}</h3>
        <ol class="steps">
          @for (step of [1, 2, 3, 4, 5]; track step) {
            <li class="card">
              <span class="step-number" aria-hidden="true">{{ step }}</span>
              <p>{{ 'lifecycle.' + step | t }}</p>
            </li>
          }
        </ol>
        <p class="muted">{{ 'home.lifecycleNote' | t }}</p>
      </div>
    </section>

    <section class="section" aria-labelledby="reports-title">
      <div class="container">
        <h2 id="reports-title">{{ 'home.reportsTitle' | t }}</h2>
        <p class="lead">{{ 'home.reportsLead' | t }}</p>
        <p class="quote">{{ 'home.reportsQuote' | t }}</p>
        <div class="table-wrap" tabindex="0" role="region" aria-labelledby="reports-title">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'report.colName' | t }}</th>
                <th scope="col">{{ 'report.colShows' | t }}</th>
                <th scope="col">{{ 'report.colFor' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of reports; track row) {
                <tr>
                  <th scope="row">{{ 'report.' + row + '.name' | t }}</th>
                  <td>{{ 'report.' + row + '.shows' | t }}</td>
                  <td>{{ 'report.' + row + '.for' | t }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <section class="section alt" aria-labelledby="benefits-title">
      <div class="container">
        <h2 id="benefits-title">{{ 'home.benefitsTitle' | t }}</h2>
        <p class="lead">{{ 'home.benefitsLead' | t }}</p>
        <div class="grid-3">
          @for (benefit of [1, 2, 3, 4, 5, 6]; track benefit) {
            <article class="card">
              <h3>{{ 'benefit.' + benefit + '.title' | t }}</h3>
              <p class="muted">{{ 'benefit.' + benefit + '.body' | t }}</p>
            </article>
          }
        </div>
        <h3 class="section-title" id="roi-title">{{ 'roi.title' | t }}</h3>
        <p class="muted">{{ 'roi.example' | t }}</p>
        <div class="grid-3">
          @for (stat of [1, 2, 3]; track stat) {
            <div class="card stat">
              <strong>{{ 'roi.' + stat + '.value' | t }}</strong>
              <span class="muted">{{ 'roi.' + stat + '.label' | t }}</span>
            </div>
          }
        </div>
        <div class="table-wrap note" tabindex="0" role="region" aria-labelledby="roi-title">
          <table>
            <thead>
              <tr>
                <th scope="col">
                  <span class="visually-hidden">{{ 'roi.colMeasure' | t }}</span>
                </th>
                <th scope="col">{{ 'roi.colWithout' | t }}</th>
                <th scope="col">{{ 'roi.colWith' | t }}</th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <th scope="row">{{ 'roi.downtime' | t }}</th>
                <td>{{ 'roi.downtimeWithout' | t }}</td>
                <td>{{ 'roi.downtimeWith' | t }}</td>
              </tr>
              <tr>
                <th scope="row">{{ 'roi.visits' | t }}</th>
                <td>{{ 'roi.visitsWithout' | t }}</td>
                <td>{{ 'roi.visitsWith' | t }}</td>
              </tr>
            </tbody>
          </table>
        </div>
        <p class="muted note">{{ 'roi.note' | t }}</p>
      </div>
    </section>

    <section class="section" aria-labelledby="steps-title">
      <div class="container">
        <h2 id="steps-title">{{ 'home.stepsTitle' | t }}</h2>
        <p class="lead">{{ 'home.stepsLead' | t }}</p>
        <ol class="steps">
          @for (step of [1, 2, 3, 4]; track step) {
            <li class="card">
              <span class="step-number" aria-hidden="true">{{ step }}</span>
              <h3>{{ 'step.' + step + '.title' | t }}</h3>
              <p class="muted">{{ 'step.' + step + '.body' | t }}</p>
            </li>
          }
        </ol>
      </div>
    </section>

    <section class="section alt" aria-labelledby="security-title">
      <div class="container">
        <h2 id="security-title">{{ 'home.securityTitle' | t }}</h2>
        <p class="lead">{{ 'home.securityLead' | t }}</p>
        <div class="grid-3">
          @for (item of [1, 2, 3, 4, 5, 6]; track item) {
            <article class="card">
              <h3>{{ 'security.' + item + '.title' | t }}</h3>
              <p class="muted">{{ 'security.' + item + '.body' | t }}</p>
            </article>
          }
        </div>
      </div>
    </section>

    <section class="section" aria-labelledby="systems-title">
      <div class="container">
        <h2 id="systems-title">{{ 'home.systemsTitle' | t }}</h2>
        <p class="lead">{{ 'home.systemsLead' | t }}</p>
        <div class="table-wrap" tabindex="0" role="region" aria-labelledby="systems-title">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'system.colOs' | t }}</th>
                <th scope="col">{{ 'system.colInstall' | t }}</th>
                <th scope="col">{{ 'system.colCpu' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of [1, 2, 3, 4]; track row) {
                <tr>
                  <th scope="row">{{ 'system.' + row + '.os' | t }}</th>
                  <td>{{ 'system.' + row + '.install' | t }}</td>
                  <td>{{ 'system.' + row + '.cpu' | t }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="grid-3">
          @for (fact of [1, 2, 3]; track fact) {
            <div class="card stat">
              <strong>{{ 'systemFact.' + fact + '.value' | t }}</strong>
              <span class="muted">{{ 'systemFact.' + fact + '.label' | t }}</span>
            </div>
          }
        </div>
      </div>
    </section>

    <section class="section alt" aria-labelledby="faq-title">
      <div class="container narrow">
        <h2 id="faq-title">{{ 'home.faqTitle' | t }}</h2>
        @for (q of faqs; track q) {
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
        <div class="cta-row">
          <a class="btn" [routerLink]="['/', i18n.lang(), 'demo']">{{ 'home.ctaDemo' | t }}</a>
          <a class="btn secondary" [routerLink]="['/', i18n.lang(), 'pricing']">{{
            'home.ctaPrimary' | t
          }}</a>
        </div>
      </div>
    </section>
  `,
})
export class HomePage {
  protected readonly i18n = inject(I18n);
  protected readonly features = [
    'device',
    'network',
    'siteDevices',
    'databases',
    'web',
    'apps',
    'logs',
    'specs',
  ];
  protected readonly reports = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  protected readonly faqs = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
  // An illustration of the product, not real customer data.
  protected readonly previewRows = [
    { name: 'SRV-APP-01', label: 'Healthy', state: 'ok' },
    { name: 'Internet', label: 'Healthy', state: 'ok' },
    { name: 'SQL Server', label: 'Healthy', state: 'ok' },
    { name: 'Disk C: 82%', label: 'Warning', state: 'warn' },
    { name: 'GATE-CAM-03', label: 'Problem', state: 'bad' },
  ];

  constructor() {
    useSeo('home.title', 'home.lead');
  }
}
