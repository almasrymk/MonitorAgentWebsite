import {
  Component,
  computed,
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
  CatalogPlan,
  CompanyDetails,
  CompanyListItem,
  errorText,
  I18n,
  LicenseSummary,
  Loader,
  LoadError,
  MoneyPipe,
  Order,
  Paged,
  Session,
  Skeleton,
  StatusBadge,
  TranslatePipe,
  WhenPipe,
} from 'shared';

const pageSize = 25;

/** Every customer company, newest first. Search by part of the name, or by a person's exact email. */
@Component({
  selector: 'admin-customers',
  imports: [FormsModule, RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="page-head">
      <h1>{{ 'customers.title' | t }}</h1>
      <form class="row" role="search" (ngSubmit)="find()">
        <label class="visually-hidden" for="customer-search">{{ 'customers.search' | t }}</label>
        <input
          id="customer-search"
          name="search"
          type="search"
          maxlength="150"
          [placeholder]="'customers.search' | t"
          [(ngModel)]="text"
        />
        <button class="btn secondary" type="submit">{{ 'customers.find' | t }}</button>
      </form>
    </div>
    @if (companies.loading()) {
      <ma-skeleton />
    } @else if (companies.error()) {
      <ma-load-error [error]="companies.error()" (retry)="companies.load()" />
    } @else if (companies.value(); as value) {
      @if (value.items.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      } @else {
        <div class="card table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'customers.company' | t }}</th>
                <th scope="col">{{ 'customers.owner' | t }}</th>
                <th scope="col">{{ 'customers.members' | t }}</th>
                <th scope="col">{{ 'customers.since' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (company of value.items; track company.id) {
                <tr>
                  <td>
                    <a [routerLink]="['/customers', company.id]">{{ company.name }}</a>
                  </td>
                  <td>
                    {{ company.ownerName ?? '—' }}
                    <span class="muted" dir="ltr">{{ company.ownerEmail }}</span>
                  </td>
                  <td>{{ company.members }}</td>
                  <td>{{ company.createdAt | when }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (pages(value) > 1) {
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
    }
  `,
})
export class CustomersPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly search = input<string | undefined>(undefined);
  readonly pageParam = input<string | undefined>(undefined, { alias: 'page' });
  protected readonly page = () => Math.max(1, Number(this.pageParam() ?? 1) || 1);
  protected text = '';

  protected readonly companies = new Loader(() => {
    const query = new URLSearchParams({ page: String(this.page()), pageSize: String(pageSize) });
    if (this.search()) query.set('search', this.search()!);
    return this.api.get<Paged<CompanyListItem>>(`/api/v1/admin/customers?${query}`);
  });

  constructor() {
    effect(() => {
      this.text = this.search() ?? '';
      this.page();
      untracked(() => void this.companies.load());
    });
  }

  protected pages(value: Paged<CompanyListItem>): number {
    return Math.max(1, Math.ceil(value.totalCount / value.pageSize));
  }

  protected find(): void {
    void this.router.navigate([], {
      queryParams: { search: this.text.trim() || null, page: null },
    });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page }, queryParamsHandling: 'merge' });
  }
}

/** One company: its people, licenses and orders. Admins can also issue a license by hand here. */
@Component({
  selector: 'admin-customer',
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
    <p>
      <a routerLink="/customers">← {{ 'customers.title' | t }}</a>
    </p>
    @if (company.loading()) {
      <ma-skeleton />
    } @else if (company.error()) {
      <ma-load-error [error]="company.error()" (retry)="company.load()" />
    } @else if (company.value(); as value) {
      <h1>{{ value.name }}</h1>
      <p class="muted">{{ 'customers.sinceDate' | t: { date: (value.createdAt | when) } }}</p>

      <h2 class="section-gap" id="people-title">{{ 'customers.people' | t }}</h2>
      <div class="card table-wrap" tabindex="0" role="region" aria-labelledby="people-title">
        <table>
          <thead>
            <tr>
              <th scope="col">{{ 'customers.name' | t }}</th>
              <th scope="col">{{ 'customers.email' | t }}</th>
              <th scope="col">{{ 'customers.role' | t }}</th>
              <th scope="col">{{ 'customers.joined' | t }}</th>
            </tr>
          </thead>
          <tbody>
            @for (member of value.members; track member.userId) {
              <tr>
                <td>{{ member.fullName }}</td>
                <td dir="ltr">{{ member.email }}</td>
                <td>{{ 'common.role.' + member.role | t }}</td>
                <td>{{ member.joinedAt | when }}</td>
              </tr>
            }
            @for (invite of value.pendingInvitations; track invite.email) {
              <tr>
                <td class="muted">{{ 'customers.invited' | t }}</td>
                <td dir="ltr">{{ invite.email }}</td>
                <td>{{ 'common.role.' + invite.role | t }}</td>
                <td class="muted">
                  {{ 'customers.inviteUntil' | t: { date: (invite.expiresAt | when) } }}
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <h2 class="section-gap" id="licenses-title">{{ 'customers.licenses' | t }}</h2>
      @if (licenses.loading()) {
        <ma-skeleton />
      } @else if (licenses.error()) {
        <ma-load-error [error]="licenses.error()" (retry)="licenses.load()" />
      } @else if (licenses.value(); as list) {
        @if (list.length === 0) {
          <p class="card muted">{{ 'common.none' | t }}</p>
        } @else {
          <div class="card table-wrap" tabindex="0" role="region" aria-labelledby="licenses-title">
            <table>
              <thead>
                <tr>
                  <th scope="col">{{ 'orders.license' | t }}</th>
                  <th scope="col">{{ 'orders.plan' | t }}</th>
                  <th scope="col">{{ 'orders.status' | t }}</th>
                  <th scope="col">{{ 'customers.devices' | t }}</th>
                  <th scope="col">{{ 'customers.expires' | t }}</th>
                </tr>
              </thead>
              <tbody>
                @for (license of list; track license.id) {
                  <tr>
                    <td class="mono">{{ license.licenseNumber }}</td>
                    <td>{{ license.productCode }} · {{ license.planCode }}</td>
                    <td><ma-status [value]="license.status" /></td>
                    <td>{{ license.activeDevices }} / {{ license.maxDevices ?? '∞' }}</td>
                    <td>{{ license.expiresAt | when }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }

      @if (isAdmin()) {
        <form class="card stack" style="margin-top: 1rem" (ngSubmit)="grant()">
          <h3>{{ 'customers.grant' | t }}</h3>
          <p class="muted">{{ 'customers.grantHint' | t }}</p>
          <div class="row">
            <div>
              <label for="grant-plan">{{ 'orders.plan' | t }}</label>
              <select id="grant-plan" name="plan" required [(ngModel)]="planId">
                <option value="" disabled>{{ 'customers.pickPlan' | t }}</option>
                @for (plan of plans.value() ?? []; track plan.id) {
                  <option [value]="plan.id">
                    {{ plan.productName }} · {{ plan.name }} ({{
                      plan.price | money: plan.currency
                    }})
                  </option>
                }
              </select>
            </div>
            <div style="flex: 1; min-width: 200px">
              <label for="grant-reference">{{ 'customers.grantReference' | t }}</label>
              <input
                id="grant-reference"
                name="reference"
                required
                maxlength="100"
                [(ngModel)]="reference"
              />
            </div>
          </div>
          @if (message()) {
            <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">
              {{ message() }}
            </p>
          }
          <div>
            <button class="btn" type="submit" [disabled]="busy() || !planId || !reference.trim()">
              {{ 'customers.grantButton' | t }}
            </button>
          </div>
        </form>
      }

      <h2 class="section-gap" id="orders-title">{{ 'nav.orders' | t }}</h2>
      @if (!canSeeOrders()) {
        <p class="card muted">{{ 'customers.ordersFinanceOnly' | t }}</p>
      } @else if (orders.loading()) {
        <ma-skeleton />
      } @else if (orders.error()) {
        <ma-load-error [error]="orders.error()" (retry)="orders.load()" />
      } @else if (orders.value(); as page) {
        @if (page.items.length === 0) {
          <p class="card muted">{{ 'common.none' | t }}</p>
        } @else {
          <div class="card table-wrap" tabindex="0" role="region" aria-labelledby="orders-title">
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
                @for (order of page.items; track order.id) {
                  <tr>
                    <td class="mono">{{ order.number }}</td>
                    <td>{{ order.productName }} · {{ order.planName }}</td>
                    <td>{{ order.total | money: order.currency }}</td>
                    <td><ma-status [value]="order.status" prefix="order.status." /></td>
                    <td>{{ order.placedAt | when: true }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
    }
  `,
})
export class CustomerPage {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly session = inject(Session);
  protected readonly isAdmin = computed(() => !!this.session.me()?.roles.includes('Admin'));
  /** Orders are for finance and admins; support sees people and licenses only. */
  protected readonly canSeeOrders = computed(
    () => !!this.session.me()?.roles.some((r) => r === 'Admin' || r === 'Finance'),
  );
  protected readonly company = new Loader(() =>
    this.api.get<CompanyDetails>(`/api/v1/admin/customers/${this.id()}`),
  );
  protected readonly licenses = new Loader(() =>
    this.api.get<LicenseSummary[]>(`/api/v1/admin/customers/${this.id()}/licenses`),
  );
  protected readonly orders = new Loader(() =>
    this.api.get<Paged<Order>>(`/api/v1/admin/orders?companyId=${this.id()}&pageSize=100`),
  );
  protected readonly plans = new Loader(() => this.api.get<CatalogPlan[]>('/api/v1/catalog/plans'));
  protected planId = '';
  protected reference = '';
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);

  constructor() {
    effect(() => {
      this.id();
      const orders = this.canSeeOrders();
      const admin = this.isAdmin();
      untracked(() => {
        void this.company.load();
        void this.licenses.load();
        if (orders) void this.orders.load();
        if (admin) void this.plans.load();
      });
    });
  }

  /** For a payment taken outside the website, when there is no order to mark paid. */
  protected async grant(): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      const issued = await this.api.post<{ licenseNumber: string }>(
        '/api/v1/admin/licenses/grants',
        {
          companyId: this.id(),
          planId: this.planId,
          orderReference: this.reference.trim(),
        },
      );
      this.message.set(this.i18n.t('customers.granted', { number: issued.licenseNumber }));
      this.reference = '';
      await this.licenses.load(true);
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
