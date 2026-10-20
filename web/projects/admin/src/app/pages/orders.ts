import {
  Component,
  effect,
  inject,
  input,
  signal,
  untracked,
  ChangeDetectionStrategy,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  Api,
  errorText,
  I18n,
  Loader,
  LoadError,
  MoneyPipe,
  Order,
  OrderStatus,
  Paged,
  Skeleton,
  StatusBadge,
  TranslatePipe,
  WhenPipe,
} from 'shared';

const statuses: OrderStatus[] = ['PendingPayment', 'Paid', 'Fulfilled', 'Cancelled'];
const pageSize = 25;

@Component({
  selector: 'admin-orders',
  imports: [
    FormsModule,
    RouterLink,
    TranslatePipe,
    WhenPipe,
    MoneyPipe,
    LoadError,
    Skeleton,
    StatusBadge,
  ],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="page-head">
      <h1>{{ 'orders.title' | t }}</h1>
      <div>
        <label for="status-filter">{{ 'orders.filter' | t }}</label>
        <select
          id="status-filter"
          [value]="status() ?? ''"
          (change)="filter($any($event.target).value)"
        >
          <option value="" [selected]="!status()">{{ 'orders.all' | t }}</option>
          @for (s of statuses; track s) {
            <option [value]="s" [selected]="s === status()">{{ 'order.status.' + s | t }}</option>
          }
        </select>
      </div>
    </div>
    @if (message()) {
      <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">
        {{ message() }}
      </p>
    }
    @if (orders.loading()) {
      <ma-skeleton />
    } @else if (orders.error()) {
      <ma-load-error [error]="orders.error()" (retry)="orders.load()" />
    } @else if (orders.value(); as value) {
      @if (value.items.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      } @else {
        <div class="card table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'orders.number' | t }}</th>
                <th scope="col">{{ 'customers.company' | t }}</th>
                <th scope="col">{{ 'orders.plan' | t }}</th>
                <th scope="col">{{ 'orders.total' | t }}</th>
                <th scope="col">{{ 'orders.status' | t }}</th>
                <th scope="col">{{ 'orders.placed' | t }}</th>
                <th scope="col">{{ 'orders.license' | t }}</th>
                <th scope="col">
                  <span class="visually-hidden">{{ 'orders.actions' | t }}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              @for (order of value.items; track order.id) {
                <tr>
                  <td class="mono">{{ order.number }}</td>
                  <td>
                    @if (order.companyId) {
                      <a [routerLink]="['/customers', order.companyId]">{{
                        order.companyName ?? '—'
                      }}</a>
                    } @else {
                      —
                    }
                  </td>
                  <td>{{ order.productName }} · {{ order.planName }}</td>
                  <td>{{ order.total | money: order.currency }}</td>
                  <td><ma-status [value]="order.status" prefix="order.status." /></td>
                  <td>{{ order.placedAt | when: true }}</td>
                  <td class="mono">{{ order.licenseNumber ?? '—' }}</td>
                  <td>
                    @if (order.status === 'PendingPayment') {
                      @if (recording() === order.id) {
                        <form class="row" (ngSubmit)="record(order)">
                          <label class="visually-hidden" [for]="'ref-' + order.id">{{
                            'orders.reference' | t
                          }}</label>
                          <input
                            [id]="'ref-' + order.id"
                            name="reference"
                            required
                            maxlength="100"
                            [placeholder]="'orders.reference' | t"
                            [(ngModel)]="reference"
                          />
                          <button class="btn" type="submit" [disabled]="busy()">
                            {{ 'orders.confirmPayment' | t }}
                          </button>
                          <button class="btn secondary" type="button" (click)="recording.set(null)">
                            {{ 'common.cancel' | t }}
                          </button>
                        </form>
                      } @else {
                        <button class="btn secondary" type="button" (click)="startRecording(order)">
                          {{ 'orders.recordPayment' | t }}
                        </button>
                      }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <nav class="row" style="margin-top: 1rem" [attr.aria-label]="'common.pages' | t">
          <button
            class="btn secondary"
            type="button"
            [disabled]="page() <= 1"
            (click)="go(page() - 1)"
          >
            {{ 'common.previous' | t }}
          </button>
          <span>{{ 'common.pageOf' | t: { page: page(), pages: pages(value) } }}</span>
          <button
            class="btn secondary"
            type="button"
            [disabled]="page() >= pages(value)"
            (click)="go(page() + 1)"
          >
            {{ 'common.next' | t }}
          </button>
        </nav>
      }
    }
  `,
})
export class OrdersPage {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  protected readonly statuses = statuses;
  /** From the address, so a filtered list can be bookmarked and the overview can link to it. */
  readonly status = input<OrderStatus | undefined>(undefined);
  readonly pageParam = input<string | undefined>(undefined, { alias: 'page' });
  protected readonly recording = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);
  protected reference = '';

  protected readonly page = () => Math.max(1, Number(this.pageParam() ?? 1) || 1);

  protected readonly orders = new Loader(() => {
    const query = new URLSearchParams({ page: String(this.page()), pageSize: String(pageSize) });
    if (this.status()) query.set('status', this.status()!);
    return this.api.get<Paged<Order>>(`/api/v1/admin/orders?${query}`);
  });

  constructor() {
    // Reloads whenever the filter or page in the address changes.
    effect(() => {
      this.status();
      this.page();
      untracked(() => void this.orders.load());
    });
  }

  protected pages(value: Paged<Order>): number {
    return Math.max(1, Math.ceil(value.totalCount / value.pageSize));
  }

  protected filter(status: string): void {
    void this.router.navigate([], { queryParams: { status: status || null, page: null } });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page }, queryParamsHandling: 'merge' });
  }

  protected startRecording(order: Order): void {
    this.reference = '';
    this.message.set('');
    this.recording.set(order.id);
  }

  /** For bank transfers and other payments received outside the website. Issues the invoice and license like a card payment. */
  protected async record(order: Order): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    try {
      await this.api.post<Order>(`/api/v1/admin/orders/${order.id}/manual-payment`, {
        reference: this.reference.trim(),
      });
      this.recording.set(null);
      this.message.set(this.i18n.t('orders.recorded', { number: order.number }));
      await this.orders.load(true);
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
