import { Component, inject, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n, TranslatePipe } from 'shared';
import { Icon, IconName } from '../icon';
import { useSeo } from '../seo';

type State = 'ok' | 'warn' | 'bad';

/** The home page follows the customer guide (Monitor Agent - دليل العميل): promise, problem, product, proof. */
@Component({
  selector: 'site-home',
  imports: [RouterLink, TranslatePipe, Icon],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <section class="hero">
      <div class="container hero-grid">
        <div class="hero-copy">
          <p class="platforms">
            <span class="tag mono"><site-icon name="monitor" />{{ 'home.eyebrow' | t }}</span>
            {{ 'home.platforms' | t }}
          </p>
          <h1>
            {{ 'home.titleA' | t }}
            <span class="soft">{{ 'home.titleB' | t }}</span>
            <span class="underline">{{ 'home.titleC' | t }}</span>
          </h1>
          <p class="lead">{{ 'home.lead' | t }}</p>
          <div class="cta-row">
            <a class="btn lg" [routerLink]="['/', i18n.lang(), 'pricing']"
              >{{ 'home.ctaPrimary' | t }}<site-icon name="arrow"
            /></a>
            <a class="btn secondary lg" [routerLink]="['/', i18n.lang(), 'download']"
              ><site-icon name="download" />{{ 'home.ctaSecondary' | t }}</a
            >
          </div>
          <ul class="points">
            <li><span class="dot-icon"><site-icon name="clock" /></span>{{ 'home.point1' | t }}</li>
            <li><span class="dot-icon"><site-icon name="bell" /></span>{{ 'home.point2' | t }}</li>
            <li><span class="dot-icon"><site-icon name="file" /></span>{{ 'home.point3' | t }}</li>
          </ul>
        </div>

        <!-- An illustration of the product, not real customer data. -->
        <figure class="app-window" [attr.aria-label]="'home.previewLabel' | t">
          <div class="app-bar">
            <span class="app-name"><site-icon name="pulse" />Monitor Agent</span>
            <span class="app-status"><span class="live"></span>{{ 'mock.connected' | t }}</span>
            <span class="app-controls" aria-hidden="true"
              ><site-icon name="windowMin" /><site-icon name="windowMax" /><site-icon name="x"
            /></span>
          </div>
          <div class="app-body" aria-hidden="true">
            <ul class="app-nav">
              @for (item of mockNav; track item.key) {
                <li [class.active]="item.key === 'dashboard'">
                  <site-icon [name]="item.icon" />{{ 'mock.' + item.key | t }}
                  @if (item.key === 'alerts') {
                    <span class="count">2</span>
                  }
                </li>
              }
            </ul>
            <div class="app-main">
              <div class="kpis">
                @for (kpi of kpis; track kpi.label) {
                  <div class="kpi" [class.dark]="kpi.dark">
                    <div class="kpi-head">
                      <span class="kpi-icon"><site-icon [name]="kpi.icon" /></span>
                      <span class="kpi-label">
                        @if (kpi.dark) {
                          <site-icon name="warning" />
                        }
                        {{ kpi.label | t }}
                      </span>
                    </div>
                    <strong class="mono"
                      >{{ kpi.value }}<small>{{ kpi.unit }}</small></strong
                    >
                    <svg class="spark" viewBox="0 0 100 24" preserveAspectRatio="none">
                      <path [attr.d]="kpi.spark" />
                    </svg>
                  </div>
                }
              </div>
              <div class="app-table">
                <div class="app-row head">
                  <span>{{ 'mock.colItem' | t }}</span><span>{{ 'mock.colState' | t }}</span
                  ><span>{{ 'mock.colValue' | t }}</span><span>{{ 'mock.colUpdated' | t }}</span><span></span>
                </div>
                @for (row of previewRows; track row.name) {
                  <div class="app-row" [class.flag]="row.state === 'warn'">
                    <span class="mono name"><site-icon [name]="row.icon" />{{ row.name }}</span>
                    <span><span class="pill" [class]="row.state"><site-icon [name]="stateIcon[row.state]" />{{ 'state.' + row.state + '.title' | t }}</span></span>
                    <span class="mono">{{ row.value }}</span>
                    <span class="muted">{{ 'mock.minuteAgo' | t }}</span>
                    <span class="muted"><site-icon name="chevron" /></span>
                  </div>
                }
              </div>
            </div>
          </div>
        </figure>
      </div>
    </section>

    <section class="categories" [attr.aria-label]="'home.categoriesLabel' | t">
      <ul class="container">
        @for (cat of categories; track cat.key; let last = $last) {
          <li [class.active]="last">
            <span class="cat-icon"><site-icon [name]="cat.icon" /></span>
            {{ 'cat.' + cat.key | t }}
          </li>
        }
      </ul>
    </section>

    <section class="section alt" aria-labelledby="promises-title">
      <div class="container">
        <div class="split">
          <div>
            <p class="eyebrow mono"><span dir="ltr">01 — Overview</span></p>
            <h2 id="promises-title">{{ 'home.promisesTitle' | t }}</h2>
            <p class="lead">{{ 'home.promisesLead' | t }}</p>
            <p class="muted small">{{ 'home.promisesNote' | t }}</p>
          </div>
          <div class="panel dotted diagram" aria-hidden="true">
            <div class="laptop">
              <div class="screen"><site-icon name="pulse" /></div>
              <div class="base"></div>
              <span class="chip">{{ 'diagram.site' | t }}</span>
            </div>
            <span class="dash"></span>
            <div class="stack">
              <div class="cloud"><site-icon name="pulse" /></div>
              <span></span><span></span><span></span>
              <span class="chip dark">Monitor Agent</span>
            </div>
            <span class="dash fork"></span>
            <ul class="outputs">
              <li><site-icon name="bell" />{{ 'cat.alerts' | t }}</li>
              <li><site-icon name="file" />{{ 'diagram.reports' | t }}</li>
              <li class="dark"><site-icon name="chart" />{{ 'diagram.full' | t }}</li>
            </ul>
          </div>
        </div>
        <div class="grid-3 promises">
          @for (promise of promises; track promise.key; let i = $index) {
            <article class="card">
              <div class="card-top">
                <span class="square dark"><site-icon [name]="promise.icon" /></span>
                <span class="index mono" dir="ltr">/ 0{{ i + 1 }}</span>
              </div>
              <h3>{{ 'promise.' + promise.key + '.title' | t }}</h3>
              <p class="muted">{{ 'promise.' + promise.key + '.body' | t }}</p>
            </article>
          }
        </div>
        <figure class="quote-box dotted-dark">
          <span class="quote-mark" aria-hidden="true">”</span>
          <blockquote>{{ 'home.quoteMain' | t }}</blockquote>
          <figcaption>{{ 'home.quoteSub' | t }}</figcaption>
        </figure>
      </div>
    </section>

    <section class="section" aria-labelledby="challenge-title">
      <div class="container">
        <p class="eyebrow mono"><span dir="ltr">02 — The challenge</span></p>
        <h2 id="challenge-title">
          {{ 'home.challengeTitleA' | t }} <span class="soft">{{ 'home.challengeTitleB' | t }}</span>
        </h2>
        <p class="lead">{{ 'home.challengeLead' | t }}</p>
        <div class="table-wrap panel flat" tabindex="0" role="region" aria-labelledby="challenge-title">
          <table class="flow">
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
                  <th scope="row"><span class="index mono">0{{ row }}</span>{{ 'challenge.' + row + '.situation' | t }}</th>
                  <td class="muted"><site-icon name="arrow" />{{ 'challenge.' + row + '.what' | t }}</td>
                  <td><site-icon name="arrow" class="muted" /><span class="pill bad"><site-icon name="trendDown" />{{ 'challenge.' + row + '.result' | t }}</span></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <section class="section alt" id="features" aria-labelledby="features-title">
      <div class="container">
        <div class="split">
          <div>
            <p class="eyebrow mono"><span dir="ltr">03 — Coverage</span></p>
            <h2 id="features-title">{{ 'home.featuresTitle' | t }}</h2>
            <p class="lead">{{ 'home.featuresLead' | t }}</p>
          </div>
          <div class="panel layers" aria-hidden="true">
            <div class="layers-head"><span class="mono">7 layers</span><span>{{ 'layers.top' | t }}</span></div>
            @for (layer of [1, 2, 3, 4, 5, 6]; track layer) {
              <div class="layer" [class]="'l' + layer">
                {{ 'layer.' + layer | t }}
                @if (layer === 1) {
                  <span class="mono">API</span>
                }
              </div>
            }
            <div class="layers-foot">{{ 'layers.bottom' | t }}</div>
          </div>
        </div>
        <div class="grid-4">
          @for (feature of features; track feature.key; let last = $last) {
            <article class="card" [class.dark]="last">
              <span class="square"><site-icon [name]="feature.icon" /></span>
              <h3>{{ 'feature.' + feature.key + '.title' | t }}</h3>
              <p class="muted">{{ 'feature.' + feature.key + '.body' | t }}</p>
            </article>
          }
        </div>
      </div>
    </section>

    <section class="section" aria-labelledby="alerts-title">
      <div class="container">
        <p class="eyebrow mono"><span dir="ltr">04 — Alerts</span></p>
        <h2 id="alerts-title">{{ 'home.alertsTitle' | t }}</h2>
        <p class="lead">{{ 'home.alertsLead' | t }}</p>
        <div class="grid-3">
          @for (s of states; track s.state) {
            <article class="card state" [class]="s.state">
              <span class="pill" [class]="s.state"><site-icon [name]="stateIcon[s.state]" />{{ 'state.' + s.state + '.title' | t }}</span>
              <p>{{ 'state.' + s.state + '.body' | t }}</p>
              <div class="meter" aria-hidden="true">
                <div class="meter-label"><span>{{ 'mock.disk' | t }}</span><span class="mono">{{ s.value }}%</span></div>
                <div class="meter-track"><span [style.width.%]="s.value"></span><i [style.inset-inline-start.%]="s.mark"></i></div>
              </div>
            </article>
          }
        </div>
        <div class="callout">
          <span class="square dark"><site-icon name="message" /></span>
          <p>{{ 'home.board' | t }}</p>
        </div>
        <h3 class="section-title">{{ 'home.lifecycleTitle' | t }}</h3>
        <ol class="timeline">
          @for (step of [1, 2, 3, 4, 5]; track step) {
            <li [class.current]="step === 3">
              <span class="node mono">
                @if (step === 3) {
                  <site-icon name="bell" />
                } @else {
                  {{ step }}
                }
              </span>
              <span>{{ 'lifecycle.' + step | t }}</span>
            </li>
          }
        </ol>
        <p class="muted small rule">{{ 'home.lifecycleNote' | t }}</p>
      </div>
    </section>

    <section class="section dark-section" aria-labelledby="reports-title">
      <div class="container">
        <div class="split">
          <div>
            <p class="eyebrow mono"><span dir="ltr">05 — Reports</span></p>
            <h2 id="reports-title">{{ 'home.reportsTitle' | t }}</h2>
            <p class="lead">{{ 'home.reportsLead' | t }}</p>
            <p class="callout dark"><site-icon name="shield" /><strong>{{ 'home.reportsQuote' | t }}</strong></p>
            <ul class="formats mono">
              <li>PDF</li>
              <li>HTML</li>
              <li>CSV</li>
            </ul>
          </div>
          <div class="report-sample" aria-hidden="true">
            <div class="sheet">
              <div class="sheet-head">
                <div>
                  <strong>{{ 'sample.title' | t }}</strong>
                  <span class="muted">{{ 'sample.sub' | t }}</span>
                </div>
                <span class="app-name"><site-icon name="pulse" />Monitor Agent</span>
              </div>
              <p class="rate"><strong class="mono">99.6%</strong> <span class="muted">{{ 'sample.rate' | t }}</span></p>
              <div class="bars">
                @for (day of [1, 2, 3, 4, 5, 6, 7]; track day) {
                  <div class="bar-col" [class.dip]="day === 3">
                    @if (day === 3) {
                      <span class="mono">97.1%</span>
                    }
                    <span class="bar"></span>
                    <span class="muted">{{ 'day.' + day | t }}</span>
                  </div>
                }
              </div>
              <div class="sheet-stats">
                <div><span class="muted">{{ 'sample.faults' | t }}</span><strong class="mono">3</strong></div>
                <div><span class="muted">{{ 'sample.longest' | t }}</span><strong class="mono">42m</strong></div>
                <div><span class="muted">{{ 'sample.points' | t }}</span><strong class="mono">24</strong></div>
              </div>
            </div>
          </div>
        </div>
        <div class="table-wrap panel flat dark" tabindex="0" role="region" aria-labelledby="reports-title">
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
                  <th scope="row"><span class="index mono">{{ row < 10 ? '0' + row : row }}</span>{{ 'report.' + row + '.name' | t }}</th>
                  <td>{{ 'report.' + row + '.shows' | t }}</td>
                  <td><span class="pill outline">{{ 'report.' + row + '.for' | t }}</span></td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <section class="section" aria-labelledby="benefits-title">
      <div class="container">
        <p class="eyebrow mono"><span dir="ltr">06 — Value</span></p>
        <h2 id="benefits-title">{{ 'home.benefitsTitle' | t }}</h2>
        <p class="lead">{{ 'home.benefitsLead' | t }}</p>
        <div class="benefits">
          @for (benefit of benefits; track benefit.n) {
            <article>
              <site-icon [name]="benefit.icon" />
              <div>
                <h3>{{ 'benefit.' + benefit.n + '.title' | t }}</h3>
                <p class="muted">{{ 'benefit.' + benefit.n + '.body' | t }}</p>
              </div>
            </article>
          }
        </div>
        <div class="roi">
          <h3 id="roi-title">{{ 'roi.title' | t }}</h3>
          <p class="muted small">{{ 'roi.example' | t }}</p>
          <div class="grid-3">
            @for (stat of [1, 2, 3]; track stat) {
              <div class="card stat" [class.dark]="stat === 1">
                <strong [attr.dir]="stat === 1 ? 'ltr' : null">{{ 'roi.' + stat + '.value' | t }}</strong>
                <span>{{ 'roi.' + stat + '.label' | t }}</span>
              </div>
            }
          </div>
          <div class="grid-2">
            @for (cmp of comparisons; track cmp.key) {
              <div class="card compare">
                <h4>{{ 'roi.' + cmp.key | t }}</h4>
                <div class="cmp-row">
                  <div class="meter-label"><span>{{ 'roi.colWithout' | t }}</span><span>{{ 'roi.' + cmp.key + 'Without' | t }}</span></div>
                  <div class="meter-track"><span class="gray" style="width: 100%"></span></div>
                </div>
                <div class="cmp-row">
                  <div class="meter-label"><strong>{{ 'roi.colWith' | t }}</strong><strong>{{ 'roi.' + cmp.key + 'With' | t }}</strong></div>
                  <div class="meter-track"><span [style.width.%]="cmp.with"></span></div>
                </div>
              </div>
            }
          </div>
          <p class="muted small note"><site-icon name="info" />{{ 'roi.note' | t }}</p>
        </div>
      </div>
    </section>

    <section class="section alt" aria-labelledby="steps-title">
      <div class="container">
        <div class="head-row">
          <div>
            <p class="eyebrow mono"><span dir="ltr">07 — Get started</span></p>
            <h2 id="steps-title">{{ 'home.stepsTitle' | t }}</h2>
            <p class="lead">{{ 'home.stepsLead' | t }}</p>
          </div>
          <a class="btn" [routerLink]="['/', i18n.lang(), 'download']"
            ><site-icon name="download" />{{ 'home.downloadCta' | t }}</a
          >
        </div>
        <ol class="grid-4 steps">
          @for (step of steps; track step.n; let last = $last) {
            <li class="card" [class.dark]="last">
              <div class="card-top">
                <span class="big mono">0{{ step.n }}</span>
                <site-icon [name]="step.icon" />
              </div>
              <h3>{{ 'step.' + step.n + '.title' | t }}</h3>
              <p class="muted">{{ 'step.' + step.n + '.body' | t }}</p>
            </li>
          }
        </ol>
      </div>
    </section>

    <section class="section" aria-labelledby="security-title">
      <div class="container security">
        <div>
          <p class="eyebrow mono"><span dir="ltr">08 — Security</span></p>
          <h2 id="security-title">{{ 'home.securityTitle' | t }}</h2>
          <p class="lead">{{ 'home.securityLead' | t }}</p>
          <div class="panel dotted vault" aria-hidden="true">
            <span class="ring"><span class="core"><site-icon name="shield" /></span></span>
            <span class="chip top"><site-icon name="lock" />{{ 'security.encrypted' | t }}</span>
            <span class="chip dark bottom"><site-icon name="monitor" />{{ 'security.local' | t }}</span>
          </div>
        </div>
        <div class="grid-2">
          @for (item of security; track item.n) {
            <article class="card">
              <site-icon [name]="item.icon" />
              <h3>{{ 'security.' + item.n + '.title' | t }}</h3>
              <p class="muted">{{ 'security.' + item.n + '.body' | t }}</p>
            </article>
          }
        </div>
      </div>
    </section>

    <section class="section alt" aria-labelledby="systems-title">
      <div class="container">
        <p class="eyebrow mono"><span dir="ltr">09 — Requirements</span></p>
        <h2 id="systems-title">{{ 'home.systemsTitle' | t }}</h2>
        <p class="lead">{{ 'home.systemsLead' | t }}</p>
        <div class="grid-4">
          @for (row of [1, 2, 3, 4]; track row) {
            <article class="card os">
              <div class="card-top">
                <span class="square dark"><site-icon name="monitor" /></span>
                <span class="tag mono">{{ 'system.' + row + '.cpu' | t }}</span>
              </div>
              <h3>{{ 'system.' + row + '.os' | t }}</h3>
              <p class="muted">{{ 'system.' + row + '.install' | t }}</p>
            </article>
          }
        </div>
        <div class="facts">
          @for (fact of [1, 2, 3]; track fact) {
            <div>
              <strong>{{ 'systemFact.' + fact + '.value' | t }}</strong>
              <span>{{ 'systemFact.' + fact + '.label' | t }}</span>
            </div>
          }
        </div>
      </div>
    </section>

    <section class="section" aria-labelledby="faq-title">
      <div class="container faq-grid">
        <div>
          <p class="eyebrow mono"><span dir="ltr">10 — FAQ</span></p>
          <h2 id="faq-title">{{ 'home.faqTitle' | t }}</h2>
          <p class="muted">{{ 'faq.more' | t }}</p>
          <a class="btn secondary" [routerLink]="['/', i18n.lang(), 'demo']"
            >{{ 'faq.contact' | t }}<site-icon name="arrow"
          /></a>
        </div>
        <div class="faqs">
          @for (q of faqs; track q; let first = $first) {
            <details class="faq" [open]="first">
              <summary>
                <span class="index mono">{{ q < 10 ? '0' + q : q }}</span>
                <span class="q">{{ 'faq.' + q + '.q' | t }}</span>
                <span class="toggle" aria-hidden="true"><site-icon name="plus" /></span>
              </summary>
              <p class="muted">{{ 'faq.' + q + '.a' | t }}</p>
            </details>
          }
        </div>
      </div>
    </section>

    <section class="final">
      <div class="container final-cta dotted-dark">
        <div>
          <h2>{{ 'home.finalTitle' | t }}</h2>
          <p>{{ 'home.finalBody' | t }}</p>
        </div>
        <div class="cta-row">
          <a class="btn light lg" [routerLink]="['/', i18n.lang(), 'demo']"
            >{{ 'home.ctaDemo' | t }}<site-icon name="arrow"
          /></a>
          <a class="btn ghost lg" [routerLink]="['/', i18n.lang(), 'pricing']">{{ 'home.ctaPrimary' | t }}</a>
        </div>
        <svg class="pulse-line" aria-hidden="true" viewBox="0 0 360 60" preserveAspectRatio="none">
          <path d="M0 40h90l14-36 26 56 22-40h208" />
        </svg>
      </div>
    </section>
  `,
})
export class HomePage {
  protected readonly i18n = inject(I18n);
  protected readonly stateIcon: Record<State, IconName> = { ok: 'check', warn: 'warning', bad: 'x' };

  protected readonly mockNav: { key: string; icon: IconName }[] = [
    { key: 'dashboard', icon: 'dashboard' },
    { key: 'devices', icon: 'monitor' },
    { key: 'services', icon: 'settings' },
    { key: 'processes', icon: 'pulse' },
    { key: 'network', icon: 'network' },
    { key: 'databases', icon: 'database' },
    { key: 'cameras', icon: 'camera' },
    { key: 'logs', icon: 'file' },
    { key: 'alerts', icon: 'bell' },
    { key: 'reports', icon: 'chart' },
    { key: 'settings', icon: 'settings' },
  ];
  // An illustration of the product, not real customer data.
  protected readonly kpis: { label: string; value: string; unit: string; icon: IconName; dark?: boolean; spark: string }[] = [
    { label: 'mock.network', value: '12', unit: 'MB/s', icon: 'globe', spark: 'M0 18 L15 16 L30 19 L45 12 L60 15 L75 11 L90 14 L100 12' },
    { label: 'mock.disk', value: '82', unit: '%', icon: 'disk', dark: true, spark: 'M0 22 L20 19 L40 17 L60 13 L80 9 L100 4' },
    { label: 'mock.memory', value: '62', unit: '%', icon: 'memory', spark: 'M0 14 L15 13 L30 15 L45 12 L60 13 L75 12 L90 14 L100 13' },
    { label: 'CPU', value: '24', unit: '%', icon: 'cpu', spark: 'M0 16 L12 12 L24 17 L36 13 L48 18 L60 11 L72 15 L84 14 L100 16' },
  ];
  protected readonly previewRows: { name: string; icon: IconName; state: State; value: string }[] = [
    { name: 'SRV-APP-01', icon: 'server', state: 'ok', value: '—' },
    { name: 'Internet', icon: 'globe', state: 'ok', value: '20 ms' },
    { name: 'SQL Server', icon: 'database', state: 'ok', value: '4 ms' },
    { name: 'Disk C:', icon: 'disk', state: 'warn', value: '82%' },
    { name: 'GATE-CAM-03', icon: 'camera', state: 'bad', value: '—' },
  ];
  protected readonly categories: { key: string; icon: IconName }[] = [
    { key: 'alerts', icon: 'bell' },
    { key: 'logs', icon: 'file' },
    { key: 'network', icon: 'globe' },
    { key: 'cameras', icon: 'camera' },
    { key: 'databases', icon: 'database' },
    { key: 'services', icon: 'settings' },
    { key: 'servers', icon: 'server' },
  ];
  protected readonly promises: { key: string; icon: IconName }[] = [
    { key: 'know', icon: 'clock' },
    { key: 'understand', icon: 'search' },
    { key: 'proof', icon: 'fileCheck' },
  ];
  protected readonly features: { key: string; icon: IconName }[] = [
    { key: 'device', icon: 'cpu' },
    { key: 'network', icon: 'wifi' },
    { key: 'siteDevices', icon: 'camera' },
    { key: 'databases', icon: 'database' },
    { key: 'web', icon: 'globe' },
    { key: 'apps', icon: 'monitor' },
    { key: 'logs', icon: 'file' },
    { key: 'specs', icon: 'checkSquare' },
  ];
  protected readonly states: { state: State; value: number; mark: number }[] = [
    { state: 'ok', value: 60, mark: 80 },
    { state: 'warn', value: 80, mark: 95 },
    { state: 'bad', value: 95, mark: 95 },
  ];
  protected readonly reports = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
  protected readonly benefits: { n: number; icon: IconName }[] = [
    { n: 1, icon: 'timer' },
    { n: 2, icon: 'pin' },
    { n: 3, icon: 'wrench' },
    { n: 4, icon: 'link' },
    { n: 5, icon: 'shield' },
    { n: 6, icon: 'file' },
  ];
  protected readonly comparisons = [
    { key: 'downtime', with: 3 },
    { key: 'visits', with: 50 },
  ];
  protected readonly steps: { n: number; icon: IconName }[] = [
    { n: 1, icon: 'key' },
    { n: 2, icon: 'settings' },
    { n: 3, icon: 'target' },
    { n: 4, icon: 'checkSquare' },
  ];
  protected readonly security: { n: number; icon: IconName }[] = [
    { n: 1, icon: 'userPlus' },
    { n: 2, icon: 'lock' },
    { n: 3, icon: 'monitor' },
    { n: 4, icon: 'globe' },
    { n: 5, icon: 'signature' },
    { n: 6, icon: 'trash' },
  ];
  protected readonly faqs = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];

  constructor() {
    useSeo('home.title', 'home.lead');
  }
}
