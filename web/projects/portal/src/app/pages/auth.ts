import { Component, inject, input, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, ApiError, errorText, I18n, saveLanguage, Session, TranslatePipe } from 'shared';

const logo = `<div class="logo"><svg aria-hidden="true" width="24" height="24" viewBox="0 0 24 24"><path d="M2 12h4l3-7 4 14 3-7h6" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/></svg>Monitor Agent</div>`;
const languageSwitch = `<button class="link-button" type="button" (click)="switchLanguage()"><span class="visually-hidden">{{ 'lang.switchLabel' | t }}: </span><span [attr.lang]="i18n.other()">{{ 'lang.switch' | t }}</span></button>`;

/** What the sign-in pages share: busy state, the error line and the language switch. */
abstract class AuthForm {
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

/** Only paths inside the portal are followed after sign-in, never another site. */
function safeReturnUrl(url: string | undefined): string {
  return url && url.startsWith('/') && !url.startsWith('//') ? url : '/';
}

@Component({
  selector: 'portal-sign-in',
  imports: [FormsModule, RouterLink, TranslatePipe],
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
                <input id="email" name="email" type="email" autocomplete="username" required [(ngModel)]="email" />
              </div>
              <div class="field">
                <label for="password">{{ 'common.password' | t }}</label>
                <input id="password" name="password" type="password" autocomplete="current-password" required [(ngModel)]="password" />
              </div>
              <label class="checkbox field"><input type="checkbox" name="remember" [(ngModel)]="remember" /> {{ 'auth.remember' | t }}</label>
              @if (error()) {
                <p class="alert" role="alert">{{ error() }}</p>
                @if (unconfirmed()) {
                  <p><button class="link-button" type="button" (click)="resend()">{{ 'auth.resend' | t }}</button></p>
                }
              }
              @if (done()) {
                <p class="alert ok" role="status">{{ done() }}</p>
              }
              <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.signIn' | t }}</button>
            </form>
            <p><a routerLink="/forgot-password">{{ 'auth.forgot' | t }}</a></p>
            <p>{{ 'auth.noAccount' | t }} <a routerLink="/register">{{ 'auth.createAccount' | t }}</a></p>
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
                  <input id="code" name="code" inputmode="numeric" autocomplete="one-time-code" maxlength="8" required [(ngModel)]="code" />
                }
              </div>
              @if (error()) {
                <p class="alert" role="alert">{{ error() }}</p>
              }
              <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.verify' | t }}</button>
            </form>
            <p>
              <button class="link-button" type="button" (click)="useRecovery.set(!useRecovery()); code = ''">
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
      if (this.useRecovery()) await this.api.post('/api/v1/auth/login/recovery-code', { code: this.code.trim() });
      else await this.api.post('/api/v1/auth/login/mfa', { code: this.code.replace(/\s/g, ''), rememberMe: this.remember });
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
    if (error instanceof ApiError && error.code === 'EMAIL_NOT_CONFIRMED') this.unconfirmed.set(true);
    if (error instanceof ApiError && error.code === 'MFA_SESSION_EXPIRED') this.step.set('password');
  }

  private async finish(): Promise<void> {
    await this.session.signedIn();
    await this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
  }
}

@Component({
  selector: 'portal-register',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <main class="auth-page">
      <div class="card auth-card">
        ${logo}
        <h1>{{ 'auth.registerTitle' | t }}</h1>
        @if (done()) {
          <p class="alert ok" role="status">{{ done() }}</p>
        } @else {
          <form (ngSubmit)="register()">
            <div class="field">
              <label for="name">{{ 'common.fullName' | t }}</label>
              <input id="name" name="name" autocomplete="name" required maxlength="100" [(ngModel)]="fullName" />
            </div>
            <div class="field">
              <label for="email">{{ 'common.email' | t }}</label>
              <input id="email" name="email" type="email" autocomplete="email" required [(ngModel)]="email" />
            </div>
            <div class="field">
              <label for="password">{{ 'common.password' | t }}</label>
              <input id="password" name="password" type="password" autocomplete="new-password" required minlength="12" aria-describedby="password-help" [(ngModel)]="password" />
              <p id="password-help" class="muted">{{ 'auth.passwordHelp' | t }}</p>
            </div>
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
            <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.register' | t }}</button>
          </form>
        }
        <p>{{ 'auth.haveAccount' | t }} <a routerLink="/sign-in">{{ 'auth.signIn' | t }}</a></p>
        ${languageSwitch}
      </div>
    </main>
  `,
})
export class RegisterPage extends AuthForm {
  protected fullName = '';
  protected email = '';
  protected password = '';

  protected register(): Promise<void> {
    return this.run(async () => {
      await this.api.post('/api/v1/auth/register', { email: this.email, password: this.password, fullName: this.fullName });
      this.done.set(this.i18n.t('auth.registered'));
    });
  }
}

@Component({
  selector: 'portal-confirm-email',
  imports: [RouterLink, TranslatePipe],
  template: `
    <main class="auth-page">
      <div class="card auth-card">
        ${logo}
        @if (error()) {
          <p class="alert" role="alert">{{ error() }}</p>
        } @else if (done()) {
          <p class="alert ok" role="status">{{ done() }}</p>
        } @else {
          <p role="status">{{ 'auth.confirming' | t }}</p>
        }
        <a class="btn" routerLink="/sign-in">{{ 'auth.signIn' | t }}</a>
      </div>
    </main>
  `,
})
export class ConfirmEmailPage extends AuthForm implements OnInit {
  readonly userId = input<string>();
  readonly token = input<string>();

  ngOnInit(): void {
    void this.run(async () => {
      await this.api.post('/api/v1/auth/confirm-email', { userId: this.userId(), token: this.token() });
      this.done.set(this.i18n.t('auth.confirmed'));
    });
  }
}

@Component({
  selector: 'portal-forgot-password',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <main class="auth-page">
      <div class="card auth-card">
        ${logo}
        <h1>{{ 'auth.forgotTitle' | t }}</h1>
        @if (done()) {
          <p class="alert ok" role="status">{{ done() }}</p>
        } @else {
          <p class="muted">{{ 'auth.forgotHelp' | t }}</p>
          <form (ngSubmit)="send()">
            <div class="field">
              <label for="email">{{ 'common.email' | t }}</label>
              <input id="email" name="email" type="email" autocomplete="email" required [(ngModel)]="email" />
            </div>
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
            <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.sendLink' | t }}</button>
          </form>
        }
        <p><a routerLink="/sign-in">{{ 'auth.signIn' | t }}</a></p>
        ${languageSwitch}
      </div>
    </main>
  `,
})
export class ForgotPasswordPage extends AuthForm {
  protected email = '';

  protected send(): Promise<void> {
    return this.run(async () => {
      await this.api.post('/api/v1/auth/forgot-password', { email: this.email });
      this.done.set(this.i18n.t('auth.forgotSent'));
    });
  }
}

@Component({
  selector: 'portal-reset-password',
  imports: [FormsModule, RouterLink, TranslatePipe],
  template: `
    <main class="auth-page">
      <div class="card auth-card">
        ${logo}
        <h1>{{ 'auth.resetTitle' | t }}</h1>
        @if (done()) {
          <p class="alert ok" role="status">{{ done() }}</p>
          <a class="btn" routerLink="/sign-in">{{ 'auth.signIn' | t }}</a>
        } @else {
          <form (ngSubmit)="reset()">
            <div class="field">
              <label for="password">{{ 'auth.newPassword' | t }}</label>
              <input id="password" name="password" type="password" autocomplete="new-password" required minlength="12" aria-describedby="password-help" [(ngModel)]="password" />
              <p id="password-help" class="muted">{{ 'auth.passwordHelp' | t }}</p>
            </div>
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
            <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.reset' | t }}</button>
          </form>
        }
        ${languageSwitch}
      </div>
    </main>
  `,
})
export class ResetPasswordPage extends AuthForm {
  readonly email = input<string>();
  readonly token = input<string>();
  protected password = '';

  protected reset(): Promise<void> {
    return this.run(async () => {
      await this.api.post('/api/v1/auth/reset-password', { email: this.email(), token: this.token(), newPassword: this.password });
      this.done.set(this.i18n.t('auth.resetDone'));
    });
  }
}
