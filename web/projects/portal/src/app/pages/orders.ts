import { Component, computed, DestroyRef, inject, input, OnInit, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, CatalogPlan, errorText, I18n, Loader, MoneyPipe, Order, TranslatePipe, WhenPipe } from 'shared';
import { LoadError, Skeleton, StatusBadge } from './ui';

/** Sends the browser to the provider's hosted payment page for this order. */
async function pay(api: Api, orderId: string): Promise<void> {
  const providers = await api.get<string[]>('/api/v1/payments/providers');
  const checkout = await api.post<{ redirectUrl: string }>(`/api/v1/orders/${orderId}/pay`, { provider: providers[0] });
  window.location.assign(checkout.redirectUrl);
}

@Component({
  selector: 'portal-checkout',
  imports: [RouterLink, TranslatePipe, MoneyPipe, LoadError, Skeleton],
  template: `
    <h1>{{ 'checkout.title' | t }}</h1>
    <p class="muted">{{ 'checkout.lead' | t }}</p>
    @if (plans.loading()) {
      <portal-skeleton />
    } @else if (plans.error()) {
      <portal-load-error [error]="plans.error()" (retry)="plans.load()" />
    } @else {
      <fieldset class="card stack" style="border: 1px solid var(--line)">
        <legend class="eyebrow">{{ 'checkout.choose' | t }}</legend>
        @for (plan of plans.value(); track plan.id) {
          <label class="checkbox card" style="cursor: pointer">
            <input type="radio" name="plan" [value]="plan.id" [checked]="selectedId() === plan.id" (change)="selectedId.set(plan.id)" />
            <span style="flex: 1">
              <strong>{{ plan.productName }} · {{ plan.name }}</strong><br />
              <span class="muted">
                {{ plan.maxDevices ? ('checkout.devices' | t: { count: plan.maxDevices }) : ('checkout.unlimited' | t) }}
                @if (plan.durationDays) {
                  · {{ 'checkout.duration' | t: { days: plan.durationDays } }}
                }
              </span>
            </span>
            <strong>{{ plan.price | money: plan.currency }}</strong>
          </label>
        }
      </fieldset>
      @if (selected(); as plan) {
        <section class="card" style="margin-top: 1rem" aria-labelledby="total">
          <div class="row" style="justify-content: space-between">
            <h2 id="total">{{ 'checkout.total' | t }}</h2>
            <strong style="font-size: 1.6rem">{{ plan.price | money: plan.currency }}</strong>
          </div>
          <p class="muted">{{ 'checkout.taxNote' | t }}</p>
          @if (error()) {
            <p class="alert" role="alert">{{ error() }}</p>
          }
          <button class="btn" type="button" (click)="checkout(plan)" [disabled]="busy()">{{ 'checkout.pay' | t }}</button>
        </section>
      }
      <p><a routerLink="/orders">{{ 'orders.title' | t }}</a></p>
    }
  `,
})
export class CheckoutPage implements OnInit {
  readonly plan = input<string>();
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly plans = new Loader(() => this.api.get<CatalogPlan[]>('/api/v1/catalog/plans'));
  protected readonly selectedId = signal<string | null>(null);
  protected readonly selected = computed(() => this.plans.value()?.find((p) => p.id === this.selectedId()) ?? null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  async ngOnInit(): Promise<void> {
    await this.plans.load();
    const plans = this.plans.value() ?? [];
    this.selectedId.set(plans.find((p) => p.id === this.plan())?.id ?? plans[0]?.id ?? null);
  }

  protected async checkout(plan: CatalogPlan): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      // The server prices the order from the catalog; the browser only names the plan.
      const order = await this.api.post<Order>('/api/v1/orders', { planId: plan.id });
      await pay(this.api, order.id);
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'portal-sandbox-checkout',
  imports: [RouterLink, TranslatePipe],
  template: `
    <div class="card">
      <h1>{{ 'sandbox.title' | t }}</h1>
      <p>{{ 'sandbox.body' | t }}</p>
      <p class="mono">{{ reference() }}</p>
      <a class="btn" routerLink="/orders">{{ 'sandbox.back' | t }}</a>
    </div>
  `,
})
export class SandboxCheckoutPage {
  readonly reference = input<string>();
}

@Component({
  selector: 'portal-orders',
  imports: [RouterLink, TranslatePipe, MoneyPipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <div class="page-head">
      <h1>{{ 'orders.title' | t }}</h1>
      <a class="btn" routerLink="/checkout">{{ 'overview.buy' | t }}</a>
    </div>
    @if (orders.loading()) {
      <portal-skeleton />
    } @else if (orders.error()) {
      <portal-load-error [error]="orders.error()" (retry)="orders.load()" />
    } @else if (orders.value()?.length === 0) {
      <p class="card">{{ 'orders.empty' | t }}</p>
    } @else {
      <div class="card table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">{{ 'orders.number' | t }}</th>
              <th scope="col">{{ 'orders.plan' | t }}</th>
              <th scope="col">{{ 'orders.total' | t }}</th>
              <th scope="col">{{ 'orders.status' | t }}</th>
              <th scope="col">{{ 'orders.placed' | t }}</th>
            </tr>
          </thead>
          <tbody>
            @for (order of orders.value(); track order.id) {
              <tr>
                <td><a [routerLink]="['/orders', order.id]" class="mono">{{ order.number }}</a></td>
                <td>{{ order.productName }} · {{ order.planName }}</td>
                <td>{{ order.total | money: order.currency }}</td>
                <td><portal-status [value]="order.status" prefix="order.status." /></td>
                <td>{{ order.placedAt | when }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class OrdersPage implements OnInit {
  private readonly api = inject(Api);
  protected readonly orders = new Loader(() => this.api.get<Order[]>('/api/v1/orders'));

  ngOnInit(): void {
    void this.orders.load();
  }
}

@Component({
  selector: 'portal-order',
  imports: [RouterLink, TranslatePipe, MoneyPipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <p><a routerLink="/orders">← {{ 'orders.title' | t }}</a></p>
    @if (order.loading()) {
      <portal-skeleton />
    } @else if (order.error()) {
      <portal-load-error [error]="order.error()" (retry)="order.load()" />
    } @else if (order.value(); as value) {
      <div class="page-head">
        <h1>{{ 'order.title' | t: { number: value.number } }}</h1>
        <portal-status [value]="value.status" prefix="order.status." />
      </div>
      <div class="stack">
        <div aria-live="polite">
          @switch (value.status) {
            @case ('PendingPayment') {
              @if (value.payments.length) {
                <p class="alert" style="background: var(--warn-bg); color: var(--warn)">{{ 'order.pending' | t }}</p>
              }
            }
            @case ('Paid') {
              <p class="alert" style="background: var(--warn-bg); color: var(--warn)">{{ 'order.paid' | t }}</p>
            }
            @case ('Fulfilled') {
              <p class="alert ok">{{ 'order.fulfilled' | t: { license: value.licenseNumber ?? '' } }} <a routerLink="/licenses">{{ 'order.openLicenses' | t }}</a></p>
            }
          }
        </div>
        <section class="card">
          <dl class="dl">
            <dt>{{ 'orders.plan' | t }}</dt>
            <dd>{{ value.productName }} · {{ value.planName }}</dd>
            <dt>{{ 'orders.total' | t }}</dt>
            <dd>{{ value.total | money: value.currency }}</dd>
            <dt>{{ 'orders.placed' | t }}</dt>
            <dd>{{ value.placedAt | when: true }}</dd>
          </dl>
          @if (value.status === 'PendingPayment') {
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
            <div class="row" style="margin-top: 1rem">
              <button class="btn" type="button" (click)="payNow(value)" [disabled]="busy()">{{ 'order.pay' | t }}</button>
              <button class="btn secondary" type="button" (click)="cancel(value)" [disabled]="busy()">{{ 'order.cancel' | t }}</button>
            </div>
          }
        </section>
        @if (value.payments.length) {
          <section class="card table-wrap" aria-labelledby="payments-title">
            <h2 id="payments-title">{{ 'order.payments' | t }}</h2>
            <table>
              <tbody>
                @for (payment of value.payments; track payment.id) {
                  <tr>
                    <td>{{ payment.createdAt | when: true }}</td>
                    <td>{{ payment.amount | money: payment.currency }}</td>
                    <td><portal-status [value]="payment.status" prefix="order.payment." /></td>
                    <td>{{ payment.failureReason ?? '' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </section>
        }
      </div>
    }
  `,
})
export class OrderPage implements OnInit {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly order = new Loader(() => this.api.get<Order>(`/api/v1/orders/${this.id()}`));
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  constructor() {
    // While the provider confirms and the license is issued, check again every few seconds (for up to five minutes).
    let checks = 0;
    const timer = setInterval(() => {
      const status = this.order.value()?.status;
      const waiting = status === 'Paid' || (status === 'PendingPayment' && (this.order.value()?.payments.length ?? 0) > 0);
      if (waiting && ++checks <= 100) void this.order.load(true);
    }, 3000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  ngOnInit(): void {
    void this.order.load();
  }

  protected async payNow(order: Order): Promise<void> {
    await this.act(() => pay(this.api, order.id), true);
  }

  protected async cancel(order: Order): Promise<void> {
    if (!confirm(this.i18n.t('order.cancelConfirm'))) return;
    await this.act(async () => {
      await this.api.post(`/api/v1/orders/${order.id}/cancel`);
      await this.order.load(true);
    });
  }

  private async act(action: () => Promise<void>, keepBusy = false): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
      if (!keepBusy) this.busy.set(false);
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
      this.busy.set(false);
    }
  }
}

