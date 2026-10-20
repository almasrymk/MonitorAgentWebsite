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
import { Router } from '@angular/router';
import {
  Api,
  DemoRequest,
  DemoRequestStatus,
  errorText,
  I18n,
  Loader,
  LoadError,
  Paged,
  Skeleton,
  StatusBadge,
  TranslatePipe,
  WhenPipe,
} from 'shared';

const statuses: DemoRequestStatus[] = ['New', 'Contacted', 'Closed'];
const pageSize = 25;

/** Demo requests from the public site. Waiting ones by default, oldest at the top. */
@Component({
  selector: 'admin-demo-requests',
  imports: [FormsModule, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="page-head">
      <div>
        <h1>{{ 'demo.title' | t }}</h1>
        <p class="muted">{{ 'demo.lead' | t }}</p>
      </div>
      <div>
        <label for="demo-filter">{{ 'support.statusLabel' | t }}</label>
        <select id="demo-filter" (change)="filter($any($event.target).value)">
          <option value="all" [selected]="status() === 'all'">{{ 'orders.all' | t }}</option>
          @for (s of statuses; track s) {
            <option [value]="s" [selected]="s === status()">{{ 'demo.status.' + s | t }}</option>
          }
        </select>
      </div>
    </div>
    @if (message()) {
      <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">
        {{ message() }}
      </p>
    }
    @if (requests.loading()) {
      <ma-skeleton />
    } @else if (requests.error()) {
      <ma-load-error [error]="requests.error()" (retry)="requests.load()" />
    } @else if (requests.value(); as value) {
      @if (value.items.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      }
      <div class="stack">
        @for (r of value.items; track r.id) {
          <article class="card">
            <div class="page-head" style="margin-bottom: 0.5rem">
              <h2 style="margin: 0">{{ r.company }}</h2>
              <ma-status [value]="r.status" prefix="demo.status." />
            </div>
            <p>
              {{ r.name }} · <a [href]="'mailto:' + r.email" dir="ltr">{{ r.email }}</a>
              @if (r.phone) {
                · <a [href]="'tel:' + r.phone" dir="ltr">{{ r.phone }}</a>
              }
            </p>
            <p class="muted">
              {{ r.createdAt | when }}
              @if (r.sites) {
                · {{ 'demo.sites' | t: { count: r.sites } }}
              }
              · {{ 'demo.language' | t: { lang: r.language === 'ar' ? 'العربية' : 'English' } }}
            </p>
            @if (r.message) {
              <p class="pre">{{ r.message }}</p>
            }
            <form class="row" (ngSubmit)="save(r, $any(statusSelect).value, noteInput.value)">
              <div>
                <label [for]="'status-' + r.id">{{ 'support.statusLabel' | t }}</label>
                <select #statusSelect [id]="'status-' + r.id">
                  @for (option of statuses; track option) {
                    <option [value]="option" [selected]="option === r.status">
                      {{ 'demo.status.' + option | t }}
                    </option>
                  }
                </select>
              </div>
              <div style="flex: 1; min-width: 200px">
                <label [for]="'note-' + r.id">{{ 'demo.note' | t }}</label>
                <input
                  #noteInput
                  [id]="'note-' + r.id"
                  maxlength="1000"
                  [value]="r.staffNote ?? ''"
                />
              </div>
              <button
                class="btn secondary"
                type="submit"
                [disabled]="busy()"
                style="align-self: flex-end"
              >
                {{ 'common.save' | t }}
              </button>
            </form>
          </article>
        }
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
  `,
})
export class DemoRequestsPage {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  protected readonly statuses = statuses;
  /** "all", one status, or nothing for the default: the ones still waiting. */
  readonly statusParam = input<string | undefined>(undefined, { alias: 'status' });
  readonly pageParam = input<string | undefined>(undefined, { alias: 'page' });
  protected readonly status = () => this.statusParam() ?? 'New';
  protected readonly page = () => Math.max(1, Number(this.pageParam() ?? 1) || 1);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);

  protected readonly requests = new Loader(() => {
    const query = new URLSearchParams({ page: String(this.page()), pageSize: String(pageSize) });
    if (this.status() !== 'all') query.set('status', this.status());
    return this.api.get<Paged<DemoRequest>>(`/api/v1/admin/support/demo-requests?${query}`);
  });

  constructor() {
    effect(() => {
      this.status();
      this.page();
      untracked(() => void this.requests.load());
    });
  }

  protected pages(value: Paged<DemoRequest>): number {
    return Math.max(1, Math.ceil(value.totalCount / value.pageSize));
  }

  protected filter(status: string): void {
    void this.router.navigate([], { queryParams: { status, page: null } });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page }, queryParamsHandling: 'merge' });
  }

  protected async save(
    request: DemoRequest,
    status: DemoRequestStatus,
    note: string,
  ): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      await this.api.put(`/api/v1/admin/support/demo-requests/${request.id}`, {
        status,
        note: note.trim() || null,
      });
      this.message.set(this.i18n.t('demo.saved', { company: request.company }));
      await this.requests.load(true);
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
