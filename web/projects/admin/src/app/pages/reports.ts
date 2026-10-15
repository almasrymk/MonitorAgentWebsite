import { Component, computed, effect, inject, input, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api, Loader, LoadError, MoneyPipe, RevenueReport, Skeleton, TranslatePipe } from 'shared';

const day = (d: Date) => d.toISOString().slice(0, 10);

/** Money in and out over a period, from the ledger, per currency. The period is in the address so it can be shared. */
@Component({
  selector: 'admin-reports',
  imports: [FormsModule, TranslatePipe, MoneyPipe, LoadError, Skeleton],
  template: `
    <div class="page-head">
      <h1>{{ 'reports.title' | t }}</h1>
    </div>
    <form class="card row" (ngSubmit)="show()">
      <div>
        <label for="report-from">{{ 'reports.from' | t }}</label>
        <input id="report-from" name="from" type="date" required [(ngModel)]="fromText" />
      </div>
      <div>
        <label for="report-to">{{ 'reports.to' | t }}</label>
        <input id="report-to" name="to" type="date" required [(ngModel)]="toText" />
      </div>
      <button class="btn" type="submit" style="align-self: flex-end">{{ 'reports.show' | t }}</button>
      <a class="btn secondary" style="align-self: flex-end" [href]="csvUrl()" download>{{ 'reports.download' | t }}</a>
    </form>
    <p class="muted">{{ 'reports.utc' | t }}</p>

    @if (report.loading()) {
      <ma-skeleton />
    } @else if (report.error()) {
      <ma-load-error [error]="report.error()" (retry)="report.load()" />
    } @else if (report.value(); as value) {
      @if (value.truncated) {
        <p class="alert" role="status">{{ 'reports.truncated' | t }}</p>
      }
      @if (value.totals.length === 0) {
        <p class="card muted">{{ 'reports.empty' | t }}</p>
      } @else {
        <div class="kpis">
          @for (total of value.totals; track total.currency) {
            <article class="card kpi">
              <h2 class="muted" style="font-size: 1rem; margin: 0">{{ 'reports.net' | t: { currency: total.currency } }}</h2>
              <p class="value">{{ total.net | money: total.currency }}</p>
              <dl class="dl">
                <dt>{{ 'reports.received' | t }}</dt>
                <dd>{{ total.payments | money: total.currency }} ({{ total.paymentCount }})</dd>
                <dt>{{ 'reports.refunded' | t }}</dt>
                <dd>{{ total.refunds | money: total.currency }} ({{ total.refundCount }})</dd>
              </dl>
            </article>
          }
        </div>

        <h2 class="section-gap" id="month-title">{{ 'reports.byMonth' | t }}</h2>
        <div class="card table-wrap" tabindex="0" role="region" aria-labelledby="month-title">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'reports.month' | t }}</th>
                <th scope="col">{{ 'reports.payments' | t }}</th>
                <th scope="col">{{ 'reports.netShort' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of value.byMonth; track row.month + row.currency) {
                <tr>
                  <td>{{ row.month }}</td>
                  <td>{{ row.paymentCount }}</td>
                  <td>{{ row.net | money: row.currency }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <h2 class="section-gap" id="plan-title">{{ 'reports.byPlan' | t }}</h2>
        <div class="card table-wrap" tabindex="0" role="region" aria-labelledby="plan-title">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'orders.plan' | t }}</th>
                <th scope="col">{{ 'reports.payments' | t }}</th>
                <th scope="col">{{ 'reports.netShort' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of value.byPlan; track row.productName + row.planName + row.currency) {
                <tr>
                  <td>{{ row.productName }} · {{ row.planName }}</td>
                  <td>{{ row.paymentCount }}</td>
                  <td>{{ row.net | money: row.currency }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>

        <h2 class="section-gap" id="provider-title">{{ 'reports.byProvider' | t }}</h2>
        <div class="card table-wrap" tabindex="0" role="region" aria-labelledby="provider-title">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'reports.provider' | t }}</th>
                <th scope="col">{{ 'reports.payments' | t }}</th>
                <th scope="col">{{ 'reports.netShort' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (row of value.byProvider; track row.provider + row.currency) {
                <tr>
                  <td>{{ row.provider }}</td>
                  <td>{{ row.paymentCount }}</td>
                  <td>{{ row.net | money: row.currency }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    }
  `,
})
export class ReportsPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly from = input<string | undefined>(undefined);
  readonly to = input<string | undefined>(undefined);
  /** This month so far, unless the address says otherwise. */
  protected readonly range = computed(() => {
    const now = new Date();
    return {
      from: this.from() ?? day(new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1))),
      to: this.to() ?? day(now),
    };
  });
  protected readonly csvUrl = computed(() => `/api/v1/admin/reports/ledger.csv?${new URLSearchParams(this.range())}`);
  protected fromText = '';
  protected toText = '';

  protected readonly report = new Loader(() =>
    this.api.get<RevenueReport>(`/api/v1/admin/reports/revenue?${new URLSearchParams(this.range())}`),
  );

  constructor() {
    effect(() => {
      const { from, to } = this.range();
      untracked(() => {
        this.fromText = from;
        this.toText = to;
        void this.report.load();
      });
    });
  }

  protected show(): void {
    void this.router.navigate([], { queryParams: { from: this.fromText, to: this.toText } });
  }
}
