import { Component, effect, inject, input, signal, untracked } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  Api, errorText, I18n, Loader, LoadError, Paged, Skeleton, StatusBadge, Suggestion, SuggestionStatus, Ticket, TicketStatus, TicketSummary,
  TicketThread, TranslatePipe, WhenPipe,
} from 'shared';

const ticketStatuses: TicketStatus[] = ['Open', 'Answered', 'Closed'];
const suggestionStatuses: SuggestionStatus[] = ['New', 'Planned', 'Done', 'Declined'];
const pageSize = 25;

/** Open tickets first by default, oldest waiting at the top. */
@Component({
  selector: 'admin-support',
  imports: [RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <div class="page-head">
      <h1>{{ 'support.title' | t }}</h1>
      <div>
        <label for="ticket-filter">{{ 'support.statusLabel' | t }}</label>
        <select id="ticket-filter" (change)="filter($any($event.target).value)">
          @for (s of statuses; track s) {
            <option [value]="s" [selected]="s === current()">{{ 'support.status.' + s | t }}</option>
          }
          <option value="all" [selected]="current() === 'all'">{{ 'orders.all' | t }}</option>
        </select>
      </div>
    </div>
    @if (tickets.loading()) {
      <ma-skeleton />
    } @else if (tickets.error()) {
      <ma-load-error [error]="tickets.error()" (retry)="tickets.load()" />
    } @else if (tickets.value(); as value) {
      @if (value.items.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      } @else {
        <div class="card table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">{{ 'support.subject' | t }}</th>
                <th scope="col">{{ 'support.company' | t }}</th>
                <th scope="col">{{ 'support.categoryLabel' | t }}</th>
                <th scope="col">{{ 'support.statusLabel' | t }}</th>
                <th scope="col">{{ 'support.updated' | t }}</th>
              </tr>
            </thead>
            <tbody>
              @for (ticket of value.items; track ticket.id) {
                <tr>
                  <td><a [routerLink]="['/support', ticket.id]">{{ ticket.subject }}</a> <span class="muted mono">{{ ticket.number }}</span></td>
                  <td>{{ ticket.companyName ?? '—' }}</td>
                  <td>{{ 'support.category.' + ticket.category | t }}</td>
                  <td><ma-status [value]="ticket.status" prefix="support.status." /></td>
                  <td>{{ ticket.lastActivityAt | when: true }}</td>
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
export class SupportQueuePage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  protected readonly statuses = ticketStatuses;
  readonly status = input<string | undefined>(undefined);
  readonly pageParam = input<string | undefined>(undefined, { alias: 'page' });
  protected readonly current = () => this.status() ?? 'Open';
  protected readonly page = () => Math.max(1, Number(this.pageParam() ?? 1) || 1);

  protected readonly tickets = new Loader(() => {
    const query = new URLSearchParams({ page: String(this.page()), pageSize: String(pageSize) });
    if (this.current() !== 'all') query.set('status', this.current());
    return this.api.get<Paged<TicketSummary>>(`/api/v1/admin/support/tickets?${query}`);
  });

  constructor() {
    effect(() => {
      this.current();
      this.page();
      untracked(() => void this.tickets.load());
    });
  }

  protected pages(value: Paged<TicketSummary>): number {
    return Math.max(1, Math.ceil(value.totalCount / value.pageSize));
  }

  protected filter(status: string): void {
    void this.router.navigate([], { queryParams: { status, page: null } });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page }, queryParamsHandling: 'merge' });
  }
}

@Component({
  selector: 'admin-ticket',
  imports: [RouterLink, TranslatePipe, LoadError, Skeleton, TicketThread],
  template: `
    <p><a routerLink="/support">← {{ 'support.title' | t }}</a></p>
    @if (ticket.loading()) {
      <ma-skeleton />
    } @else if (ticket.error()) {
      <ma-load-error [error]="ticket.error()" (retry)="ticket.load()" />
    } @else if (ticket.value(); as value) {
      <ma-ticket-thread [ticket]="value" [post]="reply" (close)="close()" />
    }
  `,
})
export class AdminTicketPage {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly ticket = new Loader(() => this.api.get<Ticket>(`/api/v1/admin/support/tickets/${this.id()}`));

  protected readonly reply = async (body: string) =>
    this.ticket.set(await this.api.post<Ticket>(`/api/v1/admin/support/tickets/${this.id()}/messages`, { body }));

  constructor() {
    effect(() => {
      this.id();
      untracked(() => void this.ticket.load());
    });
  }

  protected async close(): Promise<void> {
    if (!confirm(this.i18n.t('support.closeConfirm'))) return;
    this.ticket.set(await this.api.post<Ticket>(`/api/v1/admin/support/tickets/${this.id()}/close`));
  }
}

@Component({
  selector: 'admin-suggestions',
  imports: [FormsModule, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <div class="page-head">
      <h1>{{ 'suggestions.title' | t }}</h1>
      <div>
        <label for="suggestion-filter">{{ 'support.statusLabel' | t }}</label>
        <select id="suggestion-filter" (change)="filter($any($event.target).value)">
          <option value="" [selected]="!status()">{{ 'orders.all' | t }}</option>
          @for (s of statuses; track s) {
            <option [value]="s" [selected]="s === status()">{{ 'suggestions.status.' + s | t }}</option>
          }
        </select>
      </div>
    </div>
    @if (message()) {
      <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p>
    }
    @if (suggestions.loading()) {
      <ma-skeleton />
    } @else if (suggestions.error()) {
      <ma-load-error [error]="suggestions.error()" (retry)="suggestions.load()" />
    } @else if (suggestions.value(); as value) {
      @if (value.items.length === 0) {
        <p class="card muted">{{ 'common.none' | t }}</p>
      }
      <div class="stack">
        @for (s of value.items; track s.id) {
          <article class="card">
            <div class="page-head" style="margin-bottom: 0.5rem">
              <h2 style="margin: 0">{{ s.title }}</h2>
              <ma-status [value]="s.status" prefix="suggestions.status." />
            </div>
            <p class="muted">{{ s.companyName ?? '—' }} · {{ s.createdAt | when }}</p>
            <p class="pre">{{ s.body }}</p>
            <form class="row" (ngSubmit)="answer(s, $any(statusSelect).value, noteInput.value)">
              <div>
                <label [for]="'status-' + s.id">{{ 'support.statusLabel' | t }}</label>
                <select #statusSelect [id]="'status-' + s.id">
                  @for (option of statuses; track option) {
                    <option [value]="option" [selected]="option === s.status">{{ 'suggestions.status.' + option | t }}</option>
                  }
                </select>
              </div>
              <div style="flex: 1; min-width: 200px">
                <label [for]="'note-' + s.id">{{ 'suggestions.answer' | t }}</label>
                <input #noteInput [id]="'note-' + s.id" maxlength="1000" [value]="s.staffNote ?? ''" />
              </div>
              <button class="btn secondary" type="submit" [disabled]="busy()" style="align-self: flex-end">{{ 'common.save' | t }}</button>
            </form>
          </article>
        }
      </div>
      @if (pages(value) > 1) {
        <nav class="row" style="margin-top: 1rem" [attr.aria-label]="'common.pages' | t">
          <button class="btn secondary" type="button" [disabled]="page() <= 1" (click)="go(page() - 1)">{{ 'common.previous' | t }}</button>
          <span>{{ 'common.pageOf' | t: { page: page(), pages: pages(value) } }}</span>
          <button class="btn secondary" type="button" [disabled]="page() >= pages(value)" (click)="go(page() + 1)">{{ 'common.next' | t }}</button>
        </nav>
      }
    }
  `,
})
export class AdminSuggestionsPage {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  protected readonly statuses = suggestionStatuses;
  readonly status = input<SuggestionStatus | undefined>(undefined);
  readonly pageParam = input<string | undefined>(undefined, { alias: 'page' });
  protected readonly page = () => Math.max(1, Number(this.pageParam() ?? 1) || 1);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);

  protected readonly suggestions = new Loader(() => {
    const query = new URLSearchParams({ page: String(this.page()), pageSize: String(pageSize) });
    if (this.status()) query.set('status', this.status()!);
    return this.api.get<Paged<Suggestion>>(`/api/v1/admin/support/suggestions?${query}`);
  });

  constructor() {
    effect(() => {
      this.status();
      this.page();
      untracked(() => void this.suggestions.load());
    });
  }

  protected pages(value: Paged<Suggestion>): number {
    return Math.max(1, Math.ceil(value.totalCount / value.pageSize));
  }

  protected filter(status: string): void {
    void this.router.navigate([], { queryParams: { status: status || null, page: null } });
  }

  protected go(page: number): void {
    void this.router.navigate([], { queryParams: { page }, queryParamsHandling: 'merge' });
  }

  protected async answer(suggestion: Suggestion, status: SuggestionStatus, note: string): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      await this.api.put(`/api/v1/admin/support/suggestions/${suggestion.id}`, { status, note: note.trim() || null });
      this.message.set(this.i18n.t('suggestions.saved', { title: suggestion.title }));
      await this.suggestions.load(true);
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
