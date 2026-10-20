import { Component, inject, input, OnInit, signal, ChangeDetectionStrategy } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  Api,
  Device,
  errorText,
  I18n,
  LicenseDetails,
  LicenseSummary,
  Loader,
  LoadError,
  Skeleton,
  StatusBadge,
  TranslatePipe,
  WhenPipe,
} from 'shared';

@Component({
  selector: 'portal-licenses',
  imports: [RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="page-head">
      <h1>{{ 'licenses.title' | t }}</h1>
      <a class="btn" routerLink="/checkout">{{ 'overview.buy' | t }}</a>
    </div>
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
  protected readonly licenses = new Loader(() =>
    this.api.get<LicenseSummary[]>('/api/v1/licenses'),
  );

  ngOnInit(): void {
    void this.licenses.load();
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
  private readonly i18n = inject(I18n);
  protected readonly details = new Loader(() =>
    this.api.get<LicenseDetails>(`/api/v1/licenses/${this.id()}`),
  );
  protected readonly key = signal('');
  protected readonly copied = signal(false);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);

  ngOnInit(): void {
    void this.details.load();
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
