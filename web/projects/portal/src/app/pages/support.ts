import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  Api, errorText, I18n, Loader, LoadError, Skeleton, StatusBadge, Suggestion, Ticket, TICKET_CATEGORIES, TicketCategory, TicketSummary,
  TicketThread, TranslatePipe, WhenPipe,
} from 'shared';

@Component({
  selector: 'portal-support',
  imports: [RouterLink, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <div class="page-head">
      <h1>{{ 'support.title' | t }}</h1>
      <a class="btn" routerLink="/support/new">{{ 'support.new' | t }}</a>
    </div>
    @if (tickets.loading()) {
      <ma-skeleton />
    } @else if (tickets.error()) {
      <ma-load-error [error]="tickets.error()" (retry)="tickets.load()" />
    } @else if (tickets.value()?.length === 0) {
      <p class="card">{{ 'support.none' | t }}</p>
    } @else {
      <div class="card table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">{{ 'support.subject' | t }}</th>
              <th scope="col">{{ 'support.number' | t }}</th>
              <th scope="col">{{ 'support.statusLabel' | t }}</th>
              <th scope="col">{{ 'support.updated' | t }}</th>
            </tr>
          </thead>
          <tbody>
            @for (ticket of tickets.value(); track ticket.id) {
              <tr>
                <td><a [routerLink]="['/support', ticket.id]">{{ ticket.subject }}</a></td>
                <td class="mono">{{ ticket.number }}</td>
                <td><ma-status [value]="ticket.status" prefix="support.status." /></td>
                <td>{{ ticket.lastActivityAt | when: true }}</td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class SupportPage implements OnInit {
  private readonly api = inject(Api);
  protected readonly tickets = new Loader(() => this.api.get<TicketSummary[]>('/api/v1/support/tickets'));

  ngOnInit(): void {
    void this.tickets.load();
  }
}

@Component({
  selector: 'portal-new-ticket',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <p><a routerLink="/support">← {{ 'support.title' | t }}</a></p>
    <h1>{{ 'support.new' | t }}</h1>
    <form class="card" (ngSubmit)="submit()">
      <div class="field">
        <label for="subject">{{ 'support.subject' | t }}</label>
        <input id="subject" name="subject" required maxlength="150" [(ngModel)]="subject" />
      </div>
      <div class="field">
        <label for="category">{{ 'support.categoryLabel' | t }}</label>
        <select id="category" name="category" [(ngModel)]="category">
          @for (c of categories; track c) {
            <option [value]="c">{{ 'support.category.' + c | t }}</option>
          }
        </select>
      </div>
      <div class="field">
        <label for="body">{{ 'support.message' | t }}</label>
        <textarea id="body" name="body" rows="8" required maxlength="5000" aria-describedby="body-help" [(ngModel)]="body"></textarea>
        <p id="body-help" class="muted">{{ 'support.messageHelp' | t }}</p>
      </div>
      @if (error()) {
        <p class="alert" role="alert">{{ error() }}</p>
      }
      <button class="btn" type="submit" [disabled]="busy()">{{ 'support.submit' | t }}</button>
    </form>
  `,
})
export class NewTicketPage {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  protected readonly categories = TICKET_CATEGORIES;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected subject = '';
  protected category: TicketCategory = 'Technical';
  protected body = '';

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const ticket = await this.api.post<Ticket>('/api/v1/support/tickets', { subject: this.subject, category: this.category, body: this.body });
      await this.router.navigate(['/support', ticket.id]);
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}

@Component({
  selector: 'portal-ticket',
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
export class TicketPage implements OnInit {
  readonly id = input.required<string>();
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly ticket = new Loader(() => this.api.get<Ticket>(`/api/v1/support/tickets/${this.id()}`));

  protected readonly reply = async (body: string) =>
    this.ticket.set(await this.api.post<Ticket>(`/api/v1/support/tickets/${this.id()}/messages`, { body }));

  ngOnInit(): void {
    void this.ticket.load();
  }

  protected async close(): Promise<void> {
    if (!confirm(this.i18n.t('support.closeConfirm'))) return;
    this.ticket.set(await this.api.post<Ticket>(`/api/v1/support/tickets/${this.id()}/close`));
  }
}

@Component({
  selector: 'portal-suggestions',
  imports: [FormsModule, TranslatePipe, WhenPipe, LoadError, Skeleton, StatusBadge],
  template: `
    <h1>{{ 'suggestions.title' | t }}</h1>
    <p class="muted">{{ 'suggestions.lead' | t }}</p>
    <div class="stack">
      <form class="card" (ngSubmit)="submit()" aria-labelledby="new-title">
        <h2 id="new-title">{{ 'suggestions.new' | t }}</h2>
        <div class="field">
          <label for="title">{{ 'suggestions.titleLabel' | t }}</label>
          <input id="title" name="title" required maxlength="150" [(ngModel)]="title" />
        </div>
        <div class="field">
          <label for="details">{{ 'suggestions.body' | t }}</label>
          <textarea id="details" name="details" rows="4" required maxlength="5000" [(ngModel)]="body"></textarea>
        </div>
        @if (message()) {
          <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p>
        }
        <button class="btn" type="submit" [disabled]="busy()">{{ 'suggestions.submit' | t }}</button>
      </form>

      @if (suggestions.loading()) {
        <ma-skeleton />
      } @else if (suggestions.error()) {
        <ma-load-error [error]="suggestions.error()" (retry)="suggestions.load()" />
      } @else if (suggestions.value()?.length === 0) {
        <p class="card muted">{{ 'suggestions.none' | t }}</p>
      } @else {
        @for (s of suggestions.value(); track s.id) {
          <article class="card">
            <div class="page-head" style="margin-bottom: 0.5rem">
              <h2 style="margin: 0">{{ s.title }}</h2>
              <ma-status [value]="s.status" prefix="suggestions.status." />
            </div>
            <p class="muted">{{ s.createdAt | when }}</p>
            <p class="pre">{{ s.body }}</p>
            @if (s.staffNote) {
              <p><strong>{{ 'suggestions.answer' | t }}:</strong> {{ s.staffNote }}</p>
            }
          </article>
        }
      }
    </div>
  `,
})
export class SuggestionsPage implements OnInit {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);
  protected title = '';
  protected body = '';
  protected readonly suggestions = new Loader(() => this.api.get<Suggestion[]>('/api/v1/support/suggestions'));

  ngOnInit(): void {
    void this.suggestions.load();
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.failed.set(false);
    this.message.set('');
    try {
      await this.api.post('/api/v1/support/suggestions', { title: this.title, body: this.body });
      this.title = this.body = '';
      this.message.set(this.i18n.t('suggestions.sent'));
      await this.suggestions.load(true);
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
