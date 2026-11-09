import { Component, inject, input, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import {
  Api,
  ApiError,
  CatalogPlan,
  Company,
  Device,
  errorText,
  I18n,
  LicenseDetails,
  LicenseSummary,
  Loader,
  LoadError,
  MoneyPipe,
  Order,
  Skeleton,
  StatusBadge,
  TranslatePipe,
  WhenPipe,
} from 'shared';
import { pay } from './orders';

interface CompanySubscription {
  id: string;
  planId: string;
  planCode: string;
  planName: string;
  status: string;
  startDate: string;
  endDate: string | null;
  /** A cheaper plan that takes effect at the next renewal (INT-018). */
  scheduledPlanId?: string | null;
  /** B4: devices in use across the subscription and the plan's limit (null = unlimited). */
  activeDevices?: number;
  maxDevices?: number | null;
  /** B4: set while more devices are active than the plan allows. */
  overCapacitySince?: string | null;
}

/** INT-014: the prorated price of a dearer plan for the rest of the term. */
interface UpgradeQuote {
  subscriptionId: string;
  planId: string;
  planName: string;
  price: number;
  currency: string;
  remainingDays: number;
  termEndsAt: string;
}

@Component({
  selector: 'portal-licenses',
  imports: [RouterLink, TranslatePipe, WhenPipe, MoneyPipe, LoadError, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="page-head">
      <h1>{{ 'licenses.title' | t }}</h1>
      <a class="btn" routerLink="/checkout">{{ 'overview.buy' | t }}</a>
    </div>
    <section class="card" aria-labelledby="subscription-title">
      <h2 id="subscription-title">{{ 'licenses.subscription' | t }}</h2>
      @if (note()) {
        <p class="alert" [class.ok]="!failedNote()" [attr.role]="failedNote() ? 'alert' : 'status'">{{ note() }}</p>
      }
      @if (subscriptions().length === 0) {
        <p class="muted">{{ 'licenses.noSubscription' | t }}</p>
      }
      @for (subscription of subscriptions(); track subscription.id) {
        <p>
          <strong>{{ subscription.planName }}</strong>
          <ma-status [value]="subscription.status" />
        </p>
        @if (subscription.maxDevices) {
          <p class="muted" data-testid="device-usage">
            {{ 'licenses.deviceUsage' | t: { active: subscription.activeDevices ?? 0, max: subscription.maxDevices } }}
          </p>
        }
        @if (subscription.overCapacitySince) {
          <p class="alert" role="alert" data-testid="over-capacity">
            {{ 'licenses.overCapacity' | t: { active: subscription.activeDevices ?? 0, max: subscription.maxDevices ?? 0 } }}
          </p>
        }
        @if (subscription.scheduledPlanId) {
          <p class="muted" data-testid="scheduled-change">
            {{ 'licenses.scheduled' | t: { plan: planName(subscription.scheduledPlanId), date: (subscription.endDate | when) } }}
          </p>
          @if (canBill()) {
            <button class="btn secondary" type="button" [disabled]="busy()" (click)="keepPlan(subscription)">
              {{ 'licenses.keepPlan' | t: { plan: subscription.planName } }}
            </button>
          }
        }
        @if (canBill()) {
          <label>
            {{ 'licenses.changePlan' | t }}
            <select [value]="planId()" (change)="planId.set($any($event.target).value)">
              @for (plan of plans(); track plan.id) {
                <option [value]="plan.id">{{ plan.name }}</option>
              }
            </select>
          </label>
          <button class="btn" type="button" [disabled]="busy()" (click)="changePlan(subscription)">{{ 'licenses.changePlan' | t }}</button>
          @if (upgrade(); as quote) {
            @if (quote.subscriptionId === subscription.id) {
              <div class="alert" role="status" data-testid="upgrade-quote">
                <p>
                  {{ 'licenses.upgradeQuote' | t: { plan: quote.planName, price: (quote.price | money: quote.currency), days: quote.remainingDays, date: (quote.termEndsAt | when) } }}
                </p>
                <button class="btn" type="button" [disabled]="busy()" (click)="buyUpgrade(quote)">{{ 'licenses.upgradeBuy' | t }}</button>
                <button class="btn secondary" type="button" [disabled]="busy()" (click)="upgrade.set(null)">{{ 'licenses.upgradeDismiss' | t }}</button>
              </div>
            }
          }
          <button class="btn secondary" type="button" [disabled]="busy()" (click)="subscriptionAction(subscription, 'suspend')">{{ 'licenses.suspend' | t }}</button>
          <button class="btn secondary" type="button" [disabled]="busy()" (click)="subscriptionAction(subscription, 'resume')">{{ 'licenses.resume' | t }}</button>
          <button class="btn secondary" type="button" [disabled]="busy()" (click)="subscriptionAction(subscription, 'cancel')">{{ 'licenses.cancel' | t }}</button>
        }
        @if (canDevices()) {
          <button class="btn secondary" type="button" [disabled]="busy()" (click)="issue(subscription)">{{ 'licenses.issue' | t }}</button>
        }
      }
    </section>
    <section class="card" aria-labelledby="trial-title">
      <h2 id="trial-title">{{ 'licenses.trialTitle' | t }}</h2>
      <p class="muted">{{ 'licenses.trialHelp' | t }}</p>
      @if (trialNote()) {
        <p class="alert" [class.ok]="!trialFailed()" [attr.role]="trialFailed() ? 'alert' : 'status'" data-testid="trial-code">{{ trialNote() }}</p>
      }
      <button class="btn secondary" type="button" [disabled]="busy()" (click)="startTrial()">{{ 'licenses.trialStart' | t }}</button>
    </section>
    @if (licenses.loading()) {
      <ma-skeleton />
    } @else if (licenses.error()) {
      <ma-load-error [error]="licenses.error()" (retry)="licenses.load()" />
    } @else if (licenses.value()?.length === 0) {
      <p class="card">{{ 'licenses.empty' | t }}</p>
    } @else {
      <div class="card table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">{{ 'licenses.number' | t }}</th>
              <th scope="col">{{ 'licenses.plan' | t }}</th>
              <th scope="col">{{ 'licenses.status' | t }}</th>
              <th scope="col">{{ 'licenses.key' | t }}</th>
              <th scope="col">{{ 'licenses.devices' | t }}</th>
              <th scope="col">{{ 'licenses.expires' | t }}</th>
            </tr>
          </thead>
          <tbody>
            @for (license of licenses.value(); track license.id) {
              <tr>
                <td>
                  <a [routerLink]="['/licenses', license.id]">{{ license.licenseNumber }}</a>
                </td>
                <td>{{ license.productCode }} · {{ license.planCode }}</td>
                <td><ma-status [value]="license.status" /></td>
                <td class="mono">{{ license.productKeyPrefix }}••••</td>
                <td>
                  {{ license.activeDevices
                  }}{{ license.maxDevices ? ' / ' + license.maxDevices : '' }}
                </td>
                <td>{{ license.expiresAt | when }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class LicensesPage implements OnInit {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  /** INT-014: shown when the chosen plan is dearer, instead of changing anything for free. */
  protected readonly upgrade = signal<UpgradeQuote | null>(null);
  protected readonly licenses = new Loader(() =>
    this.api.get<LicenseSummary[]>('/api/v1/licenses'),
  );
  protected readonly subscriptions = signal<CompanySubscription[]>([]);
  protected readonly plans = signal<CatalogPlan[]>([]);
  protected readonly role = signal('');
  protected readonly planId = signal('');
  protected readonly note = signal('');
  protected readonly failedNote = signal(false);
  protected readonly busy = signal(false);
  protected readonly trialNote = signal('');
  protected readonly trialFailed = signal(false);
  protected readonly canBill = () => this.role() === 'Owner' || this.role() === 'Billing';
  protected readonly canDevices = () => this.role() === 'Owner' || this.role() === 'Technical';

  ngOnInit(): void {
    void this.licenses.load();
    void this.loadSubscription();
  }

  private async loadSubscription(): Promise<void> {
    const [subscriptions, company, plans] = await Promise.all([
      this.api.get<CompanySubscription[]>('/api/v1/licenses/subscription'),
      this.api.get<Company>('/api/v1/company').catch(() => null),
      this.api.get<CatalogPlan[]>('/api/v1/catalog/plans'),
    ]);
    this.subscriptions.set(subscriptions);
    this.role.set(company?.myRole ?? '');
    this.plans.set(plans);
    if (!this.planId()) this.planId.set(plans[0]?.id ?? '');
  }

  private async act(work: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.failedNote.set(false);
    try {
      await work();
      await this.loadSubscription();
      void this.licenses.load();
    } catch (error) {
      this.failedNote.set(true);
      this.note.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }

  protected changePlan(subscription: CompanySubscription): Promise<void> {
    this.upgrade.set(null);
    return this.act(async () => {
      try {
        await this.api.post(`/api/v1/licenses/subscription/${subscription.id}/change-plan`, { planId: this.planId() });
      } catch (error) {
        if (!(error instanceof ApiError) || error.code !== 'PLAN_UPGRADE_REQUIRES_PAYMENT') throw error;
        // A dearer plan is bought as a prorated upgrade; nothing changes until it is paid.
        const params = new URLSearchParams({ subscriptionId: subscription.id, planId: this.planId() });
        this.upgrade.set(await this.api.get<UpgradeQuote>(`/api/v1/orders/upgrades/quote?${params}`));
        this.note.set('');
        return;
      }
      this.note.set(this.i18n.t('licenses.updated'));
    });
  }

  /** INT-014: places the upgrade order at the quoted price and goes to payment (or to the order for a bank transfer). */
  protected buyUpgrade(quote: UpgradeQuote): Promise<void> {
    return this.act(async () => {
      const order = await this.api.post<Order>('/api/v1/orders/upgrades', { subscriptionId: quote.subscriptionId, planId: quote.planId });
      this.upgrade.set(null);
      await pay(this.api, this.router, order.id);
    });
  }

  protected planName(planId: string): string {
    return this.plans().find((plan) => plan.id === planId)?.name ?? planId;
  }

  /** Sending the current plan cancels a scheduled change. */
  protected keepPlan(subscription: CompanySubscription): Promise<void> {
    return this.act(async () => {
      await this.api.post(`/api/v1/licenses/subscription/${subscription.id}/change-plan`, { planId: subscription.planId });
      this.note.set(this.i18n.t('licenses.scheduleCancelled'));
    });
  }

  protected subscriptionAction(subscription: CompanySubscription, action: 'suspend' | 'resume' | 'cancel'): Promise<void> {
    if (action === 'cancel' && !confirm(this.i18n.t('licenses.cancelConfirm'))) return Promise.resolve();
    return this.act(async () => {
      await this.api.post(`/api/v1/licenses/subscription/${subscription.id}/${action}`);
      this.note.set(this.i18n.t('licenses.updated'));
    });
  }

  /** B5: a single-use code the Agent redeems without any shared secret (Licensing ADR-016). */
  protected async startTrial(): Promise<void> {
    this.busy.set(true);
    this.trialFailed.set(false);
    try {
      const trial = await this.api.post<{ trialCode: string; expiresAt: string; trialDays: number }>('/api/v1/licenses/trial');
      this.trialNote.set(
        this.i18n.t('licenses.trialCode', { code: trial.trialCode, date: new Date(trial.expiresAt).toLocaleString(), days: String(trial.trialDays) }),
      );
    } catch (error) {
      this.trialFailed.set(true);
      this.trialNote.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }

  protected issue(subscription: CompanySubscription): Promise<void> {
    return this.act(async () => {
      const issued = await this.api.post<{ productKey: string }>(`/api/v1/licenses/subscription/${subscription.id}/licenses`);
      this.note.set(this.i18n.t('licenses.issued', { key: issued.productKey }));
    });
  }
}

@Component({
  selector: 'portal-license',
  imports: [RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <p>
      <a routerLink="/licenses">← {{ 'licenses.title' | t }}</a>
    </p>
    @if (details.loading()) {
      <ma-skeleton />
    } @else if (details.error()) {
      <ma-load-error [error]="details.error()" (retry)="details.load()" />
    } @else if (details.value(); as value) {
      <div class="page-head">
        <h1>{{ 'license.title' | t: { number: value.license.licenseNumber } }}</h1>
        <ma-status [value]="value.license.status" />
        @if (canBill()) {
          <button class="btn" type="button" [disabled]="busy()" (click)="renew()">{{ 'license.renew' | t }}</button>
        }
      </div>
      <div class="stack">
        <section class="card">
          <dl class="dl">
            <dt>{{ 'licenses.plan' | t }}</dt>
            <dd>{{ value.license.productCode }} · {{ value.license.planCode }}</dd>
            <dt>{{ 'license.issued' | t }}</dt>
            <dd>{{ value.license.issuedAt | when }}</dd>
            <dt>{{ 'licenses.expires' | t }}</dt>
            <dd>{{ value.license.expiresAt | when }}</dd>
            <dt>{{ 'licenses.devices' | t }}</dt>
            <dd>
              {{ value.license.activeDevices
              }}{{ value.license.maxDevices ? ' / ' + value.license.maxDevices : '' }}
            </dd>
            <dt>{{ 'licenses.key' | t }}</dt>
            <dd>
              @if (key()) {
                <code class="mono" data-testid="product-key">{{ key() }}</code>
                <button class="btn secondary" type="button" (click)="copy()">
                  {{ (copied() ? 'license.copied' : 'license.copy') | t }}
                </button>
              } @else {
                <span class="mono">{{ value.license.productKeyPrefix }}••••</span>
                <button
                  class="btn secondary"
                  type="button"
                  (click)="reveal()"
                  [disabled]="busy()"
                  aria-describedby="reveal-help"
                >
                  {{ 'license.reveal' | t }}
                </button>
                <p id="reveal-help" class="muted">{{ 'license.revealHelp' | t }}</p>
              }
              <button class="btn secondary" type="button" [disabled]="busy()" (click)="regenerate()">{{ 'license.regenerate' | t }}</button>
              <button class="btn secondary" type="button" [disabled]="busy()" (click)="licenseAction('suspend')">{{ 'license.suspend' | t }}</button>
              <button class="btn secondary" type="button" [disabled]="busy()" (click)="licenseAction('resume')">{{ 'license.resume' | t }}</button>
              <button class="btn secondary" type="button" [disabled]="busy()" (click)="licenseAction('revoke')">{{ 'license.revoke' | t }}</button>
            </dd>
          </dl>
        </section>
        @if (message()) {
          <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">
            {{ message() }}
          </p>
        }
        <section class="card" aria-labelledby="devices-title">
          <h2 id="devices-title">{{ 'license.devicesTitle' | t }}</h2>
          @if (value.devices.length === 0) {
            <p class="muted">{{ 'license.noDevices' | t }}</p>
          } @else {
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">{{ 'license.device' | t }}</th>
                    <th scope="col">{{ 'license.os' | t }}</th>
                    <th scope="col">{{ 'license.version' | t }}</th>
                    <th scope="col">{{ 'license.activated' | t }}</th>
                    <th scope="col">{{ 'license.lastSeen' | t }}</th>
                    <th scope="col">
                      <span class="visually-hidden">{{ 'license.reset' | t }}</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  @for (device of value.devices; track device.id) {
                    <tr>
                      <td>
                        {{ device.deviceName ?? device.deviceId }}
                        <span class="badge" [class.ok]="device.online">{{
                          (device.online ? 'license.online' : 'license.offline') | t
                        }}</span>
                      </td>
                      <td>{{ device.operatingSystem ?? '—' }}</td>
                      <td>{{ device.appVersion ?? '—' }}</td>
                      <td>{{ device.activatedAt | when }}</td>
                      <td>{{ device.lastSeenAt | when: true }}</td>
                      <td>
                        @if (device.status === 'Active') {
                          <button
                            class="btn secondary"
                            type="button"
                            (click)="resetDevice(value, device)"
                            [disabled]="busy()"
                          >
                            {{ 'license.reset' | t }}
                          </button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        </section>
      </div>
    }
  `,
})
export class LicensePage implements OnInit {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly i18n = inject(I18n);
  protected readonly details = new Loader(() =>
    this.api.get<LicenseDetails>(`/api/v1/licenses/${this.id()}`),
  );
  protected readonly key = signal('');
  protected readonly copied = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);
  protected readonly role = signal('');
  protected readonly canBill = () => this.role() === 'Owner' || this.role() === 'Billing';

  ngOnInit(): void {
    void this.details.load();
    void this.api
      .get<Company>('/api/v1/company')
      .then((company) => this.role.set(company.myRole ?? ''))
      .catch(() => this.role.set(''));
  }

  /** INT-018: places a renewal order for this license (same key and devices) and goes to payment. */
  protected renew(): Promise<void> {
    return this.act(async () => {
      const order = await this.api.post<Order>('/api/v1/orders/renewals', { licenseId: this.id() });
      await pay(this.api, this.router, order.id);
    });
  }

  protected regenerate(): Promise<void> {
    if (!confirm(this.i18n.t('license.regenerateConfirm'))) return Promise.resolve();
    return this.act(async () => {
      const result = await this.api.post<{ productKey: string }>(`/api/v1/licenses/${this.id()}/regenerate-key`);
      this.key.set(result.productKey);
      this.message.set(this.i18n.t('license.updated'));
      await this.details.load();
    });
  }

  protected licenseAction(action: 'suspend' | 'resume' | 'revoke'): Promise<void> {
    if (action === 'revoke' && !confirm(this.i18n.t('license.revokeConfirm'))) return Promise.resolve();
    return this.act(async () => {
      await this.api.post(`/api/v1/licenses/${this.id()}/${action}`);
      this.message.set(this.i18n.t('license.updated'));
      await this.details.load();
    });
  }

  protected async reveal(): Promise<void> {
    await this.act(async () => {
      const result = await this.api.post<{ productKey: string }>(
        `/api/v1/licenses/${this.id()}/product-key`,
      );
      this.key.set(result.productKey);
    });
  }

  protected async copy(): Promise<void> {
    await navigator.clipboard?.writeText(this.key());
    this.copied.set(true);
  }

  protected async resetDevice(value: LicenseDetails, device: Device): Promise<void> {
    if (
      !confirm(
        this.i18n.t('license.resetConfirm', { device: device.deviceName ?? device.deviceId }),
      )
    )
      return;
    await this.act(async () => {
      await this.api.post(`/api/v1/licenses/${value.license.id}/devices/${device.id}/reset`);
      this.message.set(this.i18n.t('license.resetDone'));
      await this.details.load(true);
    });
  }

  private async act(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set('');
    this.failed.set(false);
    try {
      await action();
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
