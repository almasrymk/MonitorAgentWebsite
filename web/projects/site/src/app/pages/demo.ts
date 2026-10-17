import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { I18n, TranslatePipe } from 'shared';
import { useSeo } from '../seo';

/** "Request a demo", the call to action the customer guide ends with. Sales sees the request in the admin panel. */
@Component({
  selector: 'site-demo',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <section class="section">
      <div class="container narrow">
        <h1>{{ 'demo.title' | t }}</h1>
        <p class="lead">{{ 'demo.lead' | t }}</p>

        @if (sent()) {
          <div class="card alert ok" role="status">
            <h2>{{ 'demo.doneTitle' | t }}</h2>
            <p>{{ 'demo.doneBody' | t }}</p>
            <a class="btn secondary" [routerLink]="['/', i18n.lang()]">{{ 'notFound.home' | t }}</a>
          </div>
        } @else {
          <form class="card" (ngSubmit)="send()">
            <div class="field">
              <label for="name">{{ 'demo.name' | t }}</label>
              <input id="name" name="name" autocomplete="name" required maxlength="100" [(ngModel)]="form.name" />
            </div>
            <div class="field">
              <label for="company">{{ 'demo.company' | t }}</label>
              <input id="company" name="company" autocomplete="organization" required maxlength="150" [(ngModel)]="form.company" />
            </div>
            <div class="field">
              <label for="email">{{ 'demo.email' | t }}</label>
              <input id="email" name="email" type="email" autocomplete="email" required maxlength="254" dir="ltr" [(ngModel)]="form.email" />
            </div>
            <div class="field">
              <label for="phone">{{ 'demo.phone' | t }}</label>
              <input id="phone" name="phone" type="tel" autocomplete="tel" maxlength="30" dir="ltr" [(ngModel)]="form.phone" />
            </div>
            <div class="field">
              <label for="sites">{{ 'demo.sites' | t }}</label>
              <input id="sites" name="sites" type="number" inputmode="numeric" min="1" max="100000" [(ngModel)]="form.sites" />
            </div>
            <div class="field">
              <label for="message">{{ 'demo.message' | t }}</label>
              <textarea id="message" name="message" rows="4" maxlength="2000" [(ngModel)]="form.message"></textarea>
            </div>
            <!-- People never see this field; bots that fill every input do, and their request is quietly dropped. -->
            <div class="trap" aria-hidden="true">
              <label for="website">Website</label>
              <input id="website" name="website" tabindex="-1" autocomplete="off" [(ngModel)]="form.website" />
            </div>
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
            <p class="muted small">{{ 'demo.privacy' | t }} <a [routerLink]="['/', i18n.lang(), 'privacy']">{{ 'footer.privacy' | t }}</a></p>
            <button class="btn" type="submit" [disabled]="busy()">{{ (busy() ? 'demo.sending' : 'demo.submit') | t }}</button>
          </form>
        }
      </div>
    </section>
  `,
})
export class DemoPage {
  protected readonly i18n = inject(I18n);
  private readonly http = inject(HttpClient);
  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal('');
  protected readonly form = { name: '', company: '', email: '', phone: '', sites: null as number | null, message: '', website: '' };

  constructor() {
    useSeo('demo.title', 'demo.lead');
  }

  protected async send(): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      // Relative addresses, so Angular copies the CSRF cookie into the request header (it skips absolute ones).
      await firstValueFrom(this.http.get('/api/v1/auth/csrf'));
      await firstValueFrom(this.http.post('/api/v1/demo-requests', {
        ...this.form,
        phone: this.form.phone || null,
        message: this.form.message || null,
        sites: this.form.sites || null,
        language: this.i18n.lang(),
      }));
      this.sent.set(true);
    } catch (e) {
      const status = e instanceof HttpErrorResponse ? e.status : 0;
      this.error.set(this.i18n.t(status === 400 ? 'demo.invalid' : status === 429 ? 'demo.tooMany' : 'demo.error'));
    } finally {
      this.busy.set(false);
    }
  }
}
