import { Component, computed, inject, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import {
  Api,
  ApiError,
  Company,
  errorText,
  I18n,
  LicenseSummary,
  Loader,
  LoadError,
  Order,
  Session,
  Skeleton,
  TranslatePipe,
  WhenPipe,
} from 'shared';
import { signal } from '@angular/core';

interface Overview {
  company: Company | null;
  licenses: LicenseSummary[];
  orders: Order[];
}

@Component({
  selector: 'portal-overview',
  imports: [FormsModule, RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="page-head">
      <div>
        <h1>{{ 'overview.title' | t: { name: session.me()?.fullName ?? '' } }}</h1>
        <p class="muted">{{ 'overview.lead' | t }}</p>
      </div>
      @if (data.value()?.company) {
        <a class="btn" routerLink="/checkout">{{ 'overview.buy' | t }}</a>
      }
    </div>

    @if (data.loading()) {
      <ma-skeleton />
    } @else if (data.error()) {
      <ma-load-error [error]="data.error()" (retry)="data.load()" />
    } @else if (data.value(); as value) {
      @if (!value.company) {
        <section class="card" aria-labelledby="no-company">
          <h2 id="no-company">{{ 'overview.noCompanyTitle' | t }}</h2>
          <p class="muted">{{ 'overview.noCompanyBody' | t }}</p>
          <form class="row" (ngSubmit)="createCompany()">
            <div style="flex: 1; min-width: 220px">
              <label for="company-name">{{ 'company.name' | t }}</label>
              <input
                id="company-name"
                name="name"
                required
                maxlength="150"
                [(ngModel)]="companyName"
              />
            </div>
            <button class="btn" type="submit" [disabled]="busy()" style="align-self: flex-end">
              {{ 'company.create' | t }}
            </button>
          </form>
          @if (error()) {
            <p class="alert" role="alert">{{ error() }}</p>
          }
        </section>
      } @else {
        <div class="kpis">
          <div class="card kpi">
            <div class="muted">{{ 'overview.activeLicenses' | t }}</div>
            <div class="value">{{ activeLicenses() }}</div>
          </div>
          <div class="card kpi">
            <div class="muted">{{ 'overview.devices' | t }}</div>
            <div class="value">{{ devices().used }}</div>
            @if (devices().allowed) {
              <div class="muted">
                {{ 'overview.devicesOf' | t: { used: devices().used, allowed: devices().allowed } }}
              </div>
            }
          </div>
          <div class="card kpi">
            <div class="muted">{{ 'overview.pendingOrders' | t }}</div>
            <div class="value">{{ pendingOrders() }}</div>
          </div>
          <div class="card kpi">
            <div class="muted">{{ 'overview.nextExpiry' | t }}</div>
            <div class="value" style="font-size: 1.3rem">{{ nextExpiry() | when }}</div>
          </div>
        </div>
      }
    }
  `,
})
export class OverviewPage implements OnInit {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly session = inject(Session);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected companyName = '';

  protected readonly data = new Loader<Overview>(async () => {
    const company = await this.api.get<Company>('/api/v1/company').catch((error) => {
      if (error instanceof ApiError && error.code === 'NO_COMPANY') return null;
      throw error;
    });
    if (!company) return { company: null, licenses: [], orders: [] };
    // Viewers and technical members can't see orders; that's not an error on the overview.
    const [licenses, orders] = await Promise.all([
      this.api.get<LicenseSummary[]>('/api/v1/licenses'),
      this.api.get<Order[]>('/api/v1/orders').catch(() => [] as Order[]),
    ]);
    return { company, licenses, orders };
  });

  protected readonly activeLicenses = computed(
    () => this.data.value()?.licenses.filter((l) => l.status === 'Active').length ?? 0,
  );
  protected readonly devices = computed(() => {
    const licenses = this.data.value()?.licenses ?? [];
    return {
      used: licenses.reduce((sum, l) => sum + l.activeDevices, 0),
      allowed: licenses.every((l) => l.maxDevices)
        ? licenses.reduce((sum, l) => sum + (l.maxDevices ?? 0), 0)
        : 0,
    };
  });
  protected readonly pendingOrders = computed(
    () => this.data.value()?.orders.filter((o) => o.status === 'PendingPayment').length ?? 0,
  );
  protected readonly nextExpiry = computed(
    () =>
      this.data
        .value()
        ?.licenses.filter((l) => l.status === 'Active' && l.expiresAt)
        .map((l) => l.expiresAt!)
        .sort()[0] ?? null,
  );

  ngOnInit(): void {
    void this.data.load();
  }

  protected async createCompany(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.api.post('/api/v1/company', { name: this.companyName });
      await this.data.load();
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
