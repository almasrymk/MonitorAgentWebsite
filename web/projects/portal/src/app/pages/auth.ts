import { Component, input, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthForm, languageSwitch, logo, TranslatePipe } from 'shared';

export { SignInPage } from 'shared';

@Component({
  selector: 'portal-register',
  imports: [FormsModule, RouterLink, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
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
              <input
                id="name"
                name="name"
                autocomplete="name"
                required
                maxlength="100"
                [(ngModel)]="fullName"
              />
            </div>
            <div class="field">
              <label for="email">{{ 'common.email' | t }}</label>
              <input
                id="email"
                name="email"
                type="email"
                autocomplete="email"
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
                autocomplete="new-password"
                required
                minlength="12"
                aria-describedby="password-help"
                [(ngModel)]="password"
              />
              <p id="password-help" class="muted">{{ 'auth.passwordHelp' | t }}</p>
            </div>
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
            <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.register' | t }}</button>
          </form>
        }
        <p>
          {{ 'auth.haveAccount' | t }} <a routerLink="/sign-in">{{ 'auth.signIn' | t }}</a>
        </p>
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
      await this.api.post('/api/v1/auth/register', {
        email: this.email,
        password: this.password,
        fullName: this.fullName,
      });
      this.done.set(this.i18n.t('auth.registered'));
    });
  }
}

@Component({
  selector: 'portal-confirm-email',
  imports: [RouterLink, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
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
      await this.api.post('/api/v1/auth/confirm-email', {
        userId: this.userId(),
        token: this.token(),
      });
      this.done.set(this.i18n.t('auth.confirmed'));
    });
  }
}

@Component({
  selector: 'portal-forgot-password',
  imports: [FormsModule, RouterLink, TranslatePipe],
  changeDetection: ChangeDetectionStrategy.Eager,
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
              <input
                id="email"
                name="email"
                type="email"
                autocomplete="email"
                required
                [(ngModel)]="email"
              />
            </div>
            @if (error()) {
              <p class="alert" role="alert">{{ error() }}</p>
            }
            <button class="btn" type="submit" [disabled]="busy()">{{ 'auth.sendLink' | t }}</button>
          </form>
        }
        <p>
          <a routerLink="/sign-in">{{ 'auth.signIn' | t }}</a>
        </p>
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
  changeDetection: ChangeDetectionStrategy.Eager,
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
              <input
                id="password"
                name="password"
                type="password"
                autocomplete="new-password"
                required
                minlength="12"
                aria-describedby="password-help"
                [(ngModel)]="password"
              />
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
      await this.api.post('/api/v1/auth/reset-password', {
        email: this.email(),
        token: this.token(),
        newPassword: this.password,
      });
      this.done.set(this.i18n.t('auth.resetDone'));
    });
  }
}
