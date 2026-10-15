import { Component, effect, inject, input, untracked } from '@angular/core';
import { Router } from '@angular/router';
import { Api, Loader, LoadError, Paged, Skeleton, TranslatePipe, WhenPipe } from 'shared';

interface AuditEntry {
  id: string;
  at: string;
  actorUserId: string | null;
  actorName: string | null;
  actorEmail: string | null;
  action: string;
  target: string;
  detail: string | null;
  ipAddress: string | null;
}

/** Action groups for the filter; the API matches by prefix. */
const areas = ['order.', 'license.', 'release.', 'content.', 'support.', 'team.', 'company.'];
const pageSize = 50;

/** Who did what to which thing, newest first. Written only after the step succeeded. */
@Component({
  selector: 'admin-audit',
  imports: [TranslatePipe, WhenPipe, LoadError, Skeleton],
  template: `
    <div class="page-head">
      <h1>{{ 'audit.title' | t }}</h1>
      <div>
        <label for="audit-area">{{ 'audit.area' | t }}</label>
        <select id="audit-area" (change)="filter($any($event.target).value)">
          <option value="" [selected]="!action()">{{ 'orders.all' | t }}</option>
          @for (area of areas; track area) {
            <option [value]="area" [selected]="area === action()">{{ 'audit.area.' + area | t }}</option>
          }
        </select>
      </div>
    </div>
    <p class="muted">{{ 'audit.lead' | t }}</p>
    @if (actor()) {
      <p class="row">
        {{ 'audit.forActor' | t }}
        <button class="btn secondary" type="button" (click)="byActor(null)">{{ 'security.everyone' | t }}</button>
      </p>
    }
    @if (entries.loading()) {
      <ma-skeleton />
    } @else if (entries.error()) {
      <ma-load-error [error]="entries.error()" (retry)="entries.load()" />
    } @else if (entries.value(); as value) {
      @if (value.items.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      } @else {
        <div class="card table-wrap" tabindex="0" role="region" [attr.aria-label]="'audit.title' | t">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'security.when' | t }}</th>
                <th scope="col">{{ 'audit.who' | t }}</th>
                <th scope="col">{{ 'audit.what' | t }}</th>
                <th scope="col">{{ 'audit.target' | t }}</th>
                <th scope="col">{{ 'audit.detail' | t }}</th>
                <th scope="col">{{ 'security.ip' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (entry of value.items; track entry.id) {
                <tr>
                  <td>{{ entry.at | when: true }}</td>
                  <td>
                    @if (entry.actorUserId) {
                      <button class="link-button" type="button" (click)="byActor(entry.actorUserId)">{{ entry.actorName ?? entry.actorUserId.slice(0, 8) }}</button>
                      <div class="muted" dir="ltr">{{ entry.actorEmail }}</div>
                    } @else {
                      {{ 'audit.system' | t }}
                    }
                  </td>
                  <td class="mono">{{ entry.action }}</td>
                  <td class="mono">{{ entry.target }}</td>
                  <td>{{ entry.detail ?? '—' }}</td>
                  <td class="mono">{{ entry.ipAddress ?? '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        @if (pages(value) > 1) {
          <nav class="row" style="margin-top: 1rem" [attr.aria-label]="'common.pages' | t">
            <button class="btn secondary" type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">{{ 'common.previous' | t }}</button>
            <span>{{ 'common.pageOf' | t: { page: page(), pages: pages(value) } }}</span>
            <button class="btn secondary" type="button" [disabled]="page() >= pages(value)" (click)="go(page() + 1)">{{ 'common.next' | t }}</button>
          </nav>
        }
      }
    }
  `,
})
export class AuditPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  protected readonly areas = areas;
  readonly action = input<string | undefined>(undefined);
  readonly actor = input<string | undefined>(undefined);
  readonly pageParam = input<string | undefined>(undefined, { alias: 'page' });
  protected readonly page = () => Math.max(1, Number(this.pageParam() ?? 1) || 1);

  protected readonly entries = new Loader(() => {
    const query = new URLSearchParams({ page: String(this.page()), pageSize: String(pageSize) });
    if (this.action()) query.set('action', this.action()!);
    if (this.actor()) query.set('actorUserId', this.actor()!);
    return this.api.get<Paged<AuditEntry>>(`/api/v1/admin/audit?${query}`);
  });

  constructor() {
    effect(() => {
      this.action();
      this.actor();
      this.page();
      untracked(() => void this.entries.load());
    });
  }

  protected pages(value: Paged<AuditEntry>): number {
    return Math.max(1, Math.ceil(value.totalCount / value.pageSize));
  }

  protected filter(action: string): void {
    void this.router.navigate([], { queryParams: { action: action || null, page: null }, queryParamsHandling: 'merge' });
  }

  protected byActor(actor: string | null): void {
    void this.router.navigate([], { queryParams: { actor, page: null }, queryParamsHandling: 'merge' });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page }, queryParamsHandling: 'merge' });
  }
}
