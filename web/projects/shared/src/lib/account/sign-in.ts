import {
  Component,
  inject,
  InjectionToken,
  input,
  signal,
  ChangeDetectionStrategy,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, ApiError } from '../api';
import { I18n, TranslatePipe } from '../i18n';
import { errorText, saveLanguage, Session } from '../session';

export interface SignInOptions {
  /** True in the portal: links to register and to reset the password here. False in the admin app, which links to the portal. */
  selfService: boolean;
}

export const SIGN_IN_OPTIONS = new InjectionToken<SignInOptions>('SIGN_IN_OPTIONS', {
  factory: () => ({ selfService: true }),
});

export const logo = `<div class="logo"><svg aria-hidden="true" width="24" height="24" viewBox="0 0 24 24"><path d="M2 12h4l3-7 4 14 3-7h6" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/></svg>Monitor Agent</div>`;
export const languageSwitch = `<button class="link-button" type="button" (click)="switchLanguage()"><span class="visually-hidden">{{ 'lang.switchLabel' | t }}: </span><span [attr.lang]="i18n.other()">{{ 'lang.switch' | t }}</span></button>`;

/** What the sign-in pages share: busy state, the error line and the language switch. */
export abstract class AuthForm {
  protected readonly api = inject(Api);
  protected readonly i18n = inject(I18n);
  protected readonly session = inject(Session);
  protected readonly router = inject(Router);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly done = signal('');

  protected switchLanguage(): void {
    saveLanguage(this.i18n, this.i18n.other());
  }

  protected async run(action: () => Promise<void>): Promise<void> {
    if (this.busy()) return;
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(errorText(this.i18n, error));
      this.onError(error);
    } finally {
      this.busy.set(false);
    }
  }

  protected onError(_error: unknown): void {}
}

/** Only paths inside the app are followed after sign-in, never another site. */
export function safeReturnUrl(url: string | undefined): string {
  return url && url.startsWith('/') && !url.startsWith('//') ? url : '/';
}

@Component({
  selector: 'ma-sign-in',
  imports: [FormsModule, RouterLink, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <main class="auth-page">
      <div class="card auth-card">
        ${logo}
        @switch (step()) {
          @case ('password') {
            <h1>{{ 'auth.signInTitle' | t }}</h1>
            <form (ngSubmit)="signIn()">
              <div class="field">
                <label for="email">{{ 'common.email' | t }}</label>
                <input
                  id="email"
                  name="email"
                  type="email"
                  autocomplete="username"
                  required
                  [(ngModel)]="email"
                />
              </div>
              <div class="field">
                <label for="password">{{ 'common.password' | t }}</label>
                <input
                  id="password"
                  name="password"
                  type="password"
                  autocomplete="current-password"
                  required
                  [(ngModel)]="password"
                />
              </div>
              <label class="checkbox field"
                ><input type="checkbox" name="remember" [(ngModel)]="remember" />
                {{ 'auth.remember' | t }}</label
              >
              @if (error()) {
                <p class="alert" role="alert">{{ error() }}</p>
                @if (unconfirmed()) {
                  <p>
                    <button class="link-button" type="button" (click)="resend()">
                      {{ 'auth.resend' | t }}
                    </button>
                  </p>
                }
              }
              @if (done()) {
                <p class="alert ok" role="status">{{ done() }}</p>
              }
              <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.signIn' | t }}</button>
            </form>
            @if (options.selfService) {
              <p>
                <a routerLink="/forgot-password">{{ 'auth.forgot' | t }}</a>
              </p>
              <p>
                {{ 'auth.noAccount' | t }}
                <a routerLink="/register">{{ 'auth.createAccount' | t }}</a>
              </p>
            } @else {
              <p>
                <a href="/portal/forgot-password">{{ 'auth.forgot' | t }}</a>
              </p>
            }
          }
          @case ('mfa') {
            <h1>{{ 'auth.mfaTitle' | t }}</h1>
            <form (ngSubmit)="verify()">
              <div class="field">
                @if (useRecovery()) {
                  <label for="code">{{ 'auth.recoveryCode' | t }}</label>
                  <input id="code" name="code" autocomplete="off" required [(ngModel)]="code" />
                } @else {
                  <p class="muted">{{ 'auth.mfaHelp' | t }}</p>
                  <label for="code">{{ 'auth.code' | t }}</label>
                  <input
                    id="code"
                    name="code"
                    inputmode="numeric"
                    autocomplete="one-time-code"
                    maxlength="8"
                    required
                    [(ngModel)]="code"
                  />
                }
              </div>
              @if (error()) {
                <p class="alert" role="alert">{{ error() }}</p>
              }
              <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.verify' | t }}</button>
            </form>
            <p>
              <button
                class="link-button"
                type="button"
                (click)="useRecovery.set(!useRecovery()); code = ''"
              >
                {{ (useRecovery() ? 'auth.useAuthenticator' : 'auth.useRecovery') | t }}
              </button>
            </p>
          }
        }
        ${languageSwitch}
      </div>
    </main>
  `,
})
export class SignInPage extends AuthForm {
  protected readonly options = inject(SIGN_IN_OPTIONS);
  readonly returnUrl = input<string>();
  protected readonly step = signal<'password' | 'mfa'>('password');
  protected readonly useRecovery = signal(false);
  protected readonly unconfirmed = signal(false);
  protected email = '';
  protected password = '';
  protected remember = false;
  protected code = '';

  protected signIn(): Promise<void> {
    this.unconfirmed.set(false);
    this.done.set('');
    return this.run(async () => {
      const result = await this.api.post<{ requiresMfa: boolean }>('/api/v1/auth/login', {
        email: this.email,
        password: this.password,
        rememberMe: this.remember,
      });
      this.password = '';
      if (result.requiresMfa) this.step.set('mfa');
      else await this.finish();
    });
  }

  protected verify(): Promise<void> {
    return this.run(async () => {
      if (this.useRecovery())
        await this.api.post('/api/v1/auth/login/recovery-code', { code: this.code.trim() });
      else
        await this.api.post('/api/v1/auth/login/mfa', {
          code: this.code.replace(/\s/g, ''),
          rememberMe: this.remember,
        });
      await this.finish();
    });
  }

  protected resend(): Promise<void> {
    return this.run(async () => {
      await this.api.post('/api/v1/auth/resend-confirmation', { email: this.email });
      this.error.set('');
      this.done.set(this.i18n.t('auth.resent'));
    });
  }

  protected override onError(error: unknown): void {
    if (error instanceof ApiError && error.code === 'EMAIL_NOT_CONFIRMED')
      this.unconfirmed.set(true);
    if (error instanceof ApiError && error.code === 'MFA_SESSION_EXPIRED')
      this.step.set('password');
  }

  private async finish(): Promise<void> {
    await this.session.signedIn();
    await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
  }
}
