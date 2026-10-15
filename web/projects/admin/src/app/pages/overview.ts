import { Component, inject, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, compareVersions, Loader, LoadError, Order, Paged, Release, Session, Skeleton, TranslatePipe, WhenPipe } from 'shared';

interface Overview {
  awaitingPayment: number;
  awaitingLicense: number;
  releases: Release[];
}

@Component({
  selector: 'admin-overview',
  imports: [RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton],
  template: `
    <h1>{{ 'overview.title' | t: { name: session.me()?.fullName ?? '' } }}</h1>
    @if (data.loading()) {
      <ma-skeleton />
    } @else if (data.error()) {
      <ma-load-error [error]="data.error()" (retry)="data.load()" />
    } @else if (data.value(); as value) {
      <div class="kpis">
        <a class="card kpi" routerLink="/orders" [queryParams]="{ status: 'PendingPayment' }">
          <div class="muted">{{ 'overview.awaitingPayment' | t }}</div>
          <div class="value">{{ value.awaitingPayment }}</div>
        </a>
        <a class="card kpi" routerLink="/orders" [queryParams]="{ status: 'Paid' }">
          <div class="muted">{{ 'overview.awaitingLicense' | t }}</div>
          <div class="value">{{ value.awaitingLicense }}</div>
          <div class="muted">{{ 'overview.awaitingLicenseHelp' | t }}</div>
        </a>
      </div>
      <section class="card" aria-labelledby="channels-title">
        <h2 id="channels-title">{{ 'overview.channels' | t }}</h2>
        @if (value.releases.length === 0) {
          <p class="muted">{{ 'releases.none' | t }}</p>
        } @else {
          <dl class="dl">
            @for (release of value.releases; track release.id) {
              <dt>{{ release.channel }}</dt>
              <dd>{{ release.version }} · {{ release.publishedAt | when }}</dd>
            }
          </dl>
        }
      </section>
    }
  `,
})
export class OverviewPage implements OnInit {
  private readonly api = inject(Api);
  protected readonly session = inject(Session);

  protected readonly data = new Loader<Overview>(async () => {
    const [pending, paid, releases] = await Promise.all([
      this.api.get<Paged<Order>>('/api/v1/admin/orders?status=PendingPayment&pageSize=1'),
      this.api.get<Paged<Order>>('/api/v1/admin/orders?status=Paid&pageSize=1'),
      this.api.get<Release[]>('/api/v1/admin/releases'),
    ]);
    // The highest published version of each channel is what the program downloads (same rule as the API).
    const current = new Map<string, Release>();
    for (const release of releases) {
      const seen = current.get(release.channel);
      if (release.status === 'Published' && (!seen || compareVersions(release.version, seen.version) > 0)) current.set(release.channel, release);
    }
    return { awaitingPayment: pending.totalCount, awaitingLicense: paid.totalCount, releases: [...current.values()] };
  });

  ngOnInit(): void {
    void this.data.load();
  }
}
