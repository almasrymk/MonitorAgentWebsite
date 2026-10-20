import { Component, inject, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, CatalogPlan, I18n, MoneyPipe, TranslatePipe } from 'shared';
import { useSeo } from '../seo';

type State = { kind: 'loading' } | { kind: 'error' } | { kind: 'ready'; plans: CatalogPlan[] };

@Component({
  selector: 'site-pricing',
  imports: [RouterLink, TranslatePipe, MoneyPipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <section class="section">
      <div class="container">
        <h1>{{ 'pricing.title' | t }}</h1>
        <p class="lead">{{ 'pricing.lead' | t }}</p>

        @switch (state().kind) {
          @case ('loading') {
            <div class="grid-3" aria-busy="true">
              @for (i of [1, 2, 3]; track i) {
                <div class="card"><div class="skeleton" style="height: 12rem"></div></div>
              }
            </div>
          }
          @case ('error') {
            <div class="alert" role="alert">
              {{ 'pricing.error' | t }}
              <button class="btn secondary" type="button" (click)="load()">
                {{ 'pricing.retry' | t }}
              </button>
            </div>
          }
          @case ('ready') {
            @if (plans().length === 0) {
              <p class="card">{{ 'pricing.empty' | t }}</p>
            } @else {
              <div class="grid-3 plans">
                @for (plan of plans(); track plan.id) {
                  <article class="card plan" [attr.aria-labelledby]="'plan-' + plan.id">
                    <p class="eyebrow">{{ plan.productName }}</p>
                    <h2 [id]="'plan-' + plan.id">{{ plan.name }}</h2>
                    <p class="price">
                      <strong>{{ plan.price | money: plan.currency }}</strong>
                      <span class="muted">{{ period(plan) }}</span>
                    </p>
                    <p>
                      {{
                        plan.maxDevices
                          ? ('pricing.devices' | t: { count: plan.maxDevices })
                          : ('pricing.unlimitedDevices' | t)
                      }}
                    </p>
                    @if (plan.features.length) {
                      <ul class="checks">
                        @for (feature of plan.features; track feature) {
                          <li>{{ feature }}</li>
                        }
                      </ul>
                    }
                    <a class="btn" [href]="'/portal/checkout?plan=' + plan.id">{{
                      'pricing.buy' | t: { plan: plan.name }
                    }}</a>
                  </article>
                }
              </div>
            }
          }
        }
        <p class="muted note">
          {{ 'pricing.note' | t }}
          <a [routerLink]="['/', i18n.lang(), 'refunds']">{{ 'pricing.refundLink' | t }}</a>
        </p>
      </div>
    </section>
  `,
})
export class PricingPage implements OnInit {
  private readonly api = inject(Api);
  protected readonly i18n = inject(I18n);
  protected readonly state = signal<State>({ kind: 'loading' });

  constructor() {
    useSeo('pricing.title', 'pricing.lead');
  }

  ngOnInit(): void {
    void this.load();
  }

  protected plans(): CatalogPlan[] {
    const state = this.state();
    return state.kind === 'ready' ? state.plans : [];
  }

  /** Prices are shown exactly as the catalog gives them; nothing is calculated here. */
  protected period(plan: CatalogPlan): string {
    if (!plan.durationDays) return this.i18n.t('pricing.lifetime');
    if (plan.durationDays === 365) return this.i18n.t('pricing.perYear');
    if (plan.durationDays === 30) return this.i18n.t('pricing.perMonth');
    return this.i18n.t('pricing.perDays', { days: plan.durationDays });
  }

  async load(): Promise<void> {
    this.state.set({ kind: 'loading' });
    try {
      const plans = await this.api.get<CatalogPlan[]>('/api/v1/catalog/plans');
      this.state.set({ kind: 'ready', plans: [...plans].sort((a, b) => a.price - b.price) });
    } catch {
      this.state.set({ kind: 'error' });
    }
  }
}
