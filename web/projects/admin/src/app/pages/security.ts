import { Component, effect, inject, input, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { Api, Loader, LoadError, Paged, Skeleton, TranslatePipe, WhenPipe } from 'shared';

interface SecurityEvent {
  id: string;
  userId: string | null;
  type: string;
  occurredAt: string;
  ipAddress: string | null;
}

const pageSize = 50;

/** The append-only account security log: sign-ins, failures, lockouts, password and MFA changes. */
@Component({
  selector: 'admin-security',
  imports: [TranslatePipe, WhenPipe, LoadError, Skeleton],
  template: `
    <h1>{{ 'security.title' | t }}</h1>
    <p class="muted">{{ 'security.lead' | t }}</p>
    @if (userId()) {
      <p class="row">
        {{ 'security.forUser' | t: { user: userId()! } }}
        <button class="btn secondary" type="button" (click)="show(null)">{{ 'security.everyone' | t }}</button>
      </p>
    }
    @if (events.loading()) {
      <ma-skeleton />
    } @else if (events.error()) {
      <ma-load-error [error]="events.error()" (retry)="events.load()" />
    } @else if (events.value(); as value) {
      @if (value.items.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      } @else {
        <div class="card table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'security.when' | t }}</th>
                <th scope="col">{{ 'security.event' | t }}</th>
                <th scope="col">{{ 'security.user' | t }}</th>
                <th scope="col">{{ 'security.ip' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (event of value.items; track event.id) {
                <tr>
                  <td>{{ event.occurredAt | when: true }}</td>
                  <td>{{ 'security.type.' + event.type | t }}</td>
                  <td>
                    @if (event.userId) {
                      <button class="link-button mono" type="button" (click)="show(event.userId)">{{ event.userId.slice(0, 8) }}</button>
                    } @else {
                      —
                    }
                  </td>
                  <td class="mono">{{ event.ipAddress ?? '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <nav class="row" style="margin-top: 1rem" [attr.aria-label]="'common.pages' | t">
          <button class="btn secondary" type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">{{ 'common.previous' | t }}</button>
          <span>{{ 'common.pageOf' | t: { page: page(), pages: pages(value) } }}</span>
          <button class="btn secondary" type="button" [disabled]="page() >= pages(value)" (click)="go(page() + 1)">{{ 'common.next' | t }}</button>
        </nav>
      }
    }
  `,
})
export class SecurityPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly userId = input<string | undefined>(undefined, { alias: 'user' });
  readonly pageParam = input<string | undefined>(undefined, { alias: 'page' });
  protected readonly page = () => Math.max(1, Number(this.pageParam() ?? 1) || 1);

  protected readonly events = new Loader(() => {
    const query = new URLSearchParams({ page: String(this.page()), pageSize: String(pageSize) });
    if (this.userId()) query.set('userId', this.userId()!);
    return this.api.get<Paged<SecurityEvent>>(`/api/v1/admin/security-events?${query}`);
  });

  constructor() {
    effect(() => {
      this.userId();
      this.page();
      untracked(() => void this.events.load());
    });
  }

  protected pages(value: Paged<SecurityEvent>): number {
    return Math.max(1, Math.ceil(value.totalCount / value.pageSize));
  }

  protected show(userId: string | null): void {
    void this.router.navigate([], { queryParams: { user: userId, page: null } });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page }, queryParamsHandling: 'merge' });
  }
}
