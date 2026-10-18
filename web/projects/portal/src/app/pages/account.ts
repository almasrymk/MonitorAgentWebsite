import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api, errorText, I18n, Session, TranslatePipe } from 'shared';

@Component({
  selector: 'portal-account',
  imports: [FormsModule, TranslatePipe],
  template: `
    <h1>{{ 'account.title' | t }}</h1>
    <div class="stack">
      @if (message()) {
        <p class="alert" [class.ok]="!failed()" [attr.role]="failed() ? 'alert' : 'status'">{{ message() }}</p>
      }
      <section class="card" aria-labelledby="profile-title">
        <h2 id="profile-title">{{ 'account.profile' | t }}</h2>
        <dl class="dl">
          <dt>{{ 'common.fullName' | t }}</dt>
          <dd>{{ session.me()?.fullName }}</dd>
          <dt>{{ 'common.email' | t }}</dt>
          <dd>{{ session.me()?.email }}</dd>
        </dl>
      </section>

      <section class="card" aria-labelledby="password-title">
        <h2 id="password-title">{{ 'account.changePassword' | t }}</h2>
        <form (ngSubmit)="changePassword()">
          <input type="email" name="username" autocomplete="username" [value]="session.me()?.email" hidden />
          <div class="field">
            <label for="current">{{ 'account.currentPassword' | t }}</label>
            <input id="current" name="current" type="password" autocomplete="current-password" required [(ngModel)]="current" />
          </div>
          <div class="field">
            <label for="next">{{ 'auth.newPassword' | t }}</label>
            <input id="next" name="next" type="password" autocomplete="new-password" required minlength="12" aria-describedby="next-help" [(ngModel)]="next" />
            <p id="next-help" class="muted">{{ 'auth.passwordHelp' | t }}</p>
          </div>
          <button class="btn" type="submit" [disabled]="busy()">{{ 'common.save' | t }}</button>
        </form>
      </section>

      <section class="card" aria-labelledby="mfa-title">
        <h2 id="mfa-title">{{ 'account.mfa' | t }}</h2>
        @if (codes().length) {
          <p>{{ 'account.recoveryCodes' | t }}</p>
          <ul class="mono" data-testid="recovery-codes">
            @for (code of codes(); track code) {
              <li>{{ code }}</li>
            }
          </ul>
        }
        @if (session.me()?.mfaEnabled) {
          <p class="badge ok">{{ 'account.mfaOn' | t }}</p>
          <div class="row" style="margin-top: 1rem">
            <button class="btn secondary" type="button" (click)="newCodes()" [disabled]="busy()">{{ 'account.newCodes' | t }}</button>
          </div>
          <form class="row" style="margin-top: 1rem" (ngSubmit)="disableMfa()">
            <div style="flex: 1; min-width: 220px">
              <label for="mfa-password">{{ 'account.mfaDisableHelp' | t }}</label>
              <input id="mfa-password" name="password" type="password" autocomplete="current-password" required [(ngModel)]="disablePassword" />
            </div>
            <button class="btn secondary" type="submit" [disabled]="busy()" style="align-self: flex-end">{{ 'account.mfaDisable' | t }}</button>
          </form>
        } @else if (setupKey()) {
          <p>{{ 'account.mfaKey' | t }}</p>
          <p class="mono" data-testid="mfa-key">{{ setupKey() }}</p>
          <form class="row" (ngSubmit)="enableMfa()">
            <div style="flex: 1; min-width: 160px">
              <label for="mfa-code">{{ 'auth.code' | t }}</label>
              <input id="mfa-code" name="code" inputmode="numeric" autocomplete="one-time-code" maxlength="8" required [(ngModel)]="code" />
            </div>
            <button class="btn" type="submit" [disabled]="busy()" style="align-self: flex-end">{{ 'account.mfaEnable' | t }}</button>
          </form>
        } @else {
          <p class="muted">{{ 'account.mfaOff' | t }}</p>
          <button class="btn" type="button" (click)="startMfa()" [disabled]="busy()">{{ 'account.mfaStart' | t }}</button>
        }
      </section>

      <section class="card" aria-labelledby="sessions-title">
        <h2 id="sessions-title">{{ 'account.sessions' | t }}</h2>
        <p class="muted">{{ 'account.revokeHelp' | t }}</p>
        <button class="btn secondary" type="button" (click)="revokeAll()" [disabled]="busy()">{{ 'account.revokeAll' | t }}</button>
      </section>
    </div>
  `,
})
export class AccountPage implements OnInit {
  private readonly api = inject(Api);
  private readonly i18n = inject(I18n);
  private readonly router = inject(Router);
  protected readonly session = inject(Session);
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly failed = signal(false);
  protected readonly setupKey = signal('');
  protected readonly codes = signal<string[]>([]);
  protected current = '';
  protected next = '';
  protected code = '';
  protected disablePassword = '';

  ngOnInit(): void {
    void this.session.load(true);
  }

  protected changePassword(): Promise<void> {
    return this.act(async () => {
      await this.api.post('/api/v1/account/change-password', { currentPassword: this.current, newPassword: this.next });
      this.current = this.next = '';
      // Changing the password signs other browsers out and renews this session's tokens.
      await this.session.signedIn();
      this.message.set(this.i18n.t('account.passwordChanged'));
    });
  }

  protected startMfa(): Promise<void> {
    return this.act(async () => {
      const setup = await this.api.get<{ sharedKey: string; authenticatorUri: string }>('/api/v1/account/mfa/setup');
      await this.session.refreshCsrf();
      this.setupKey.set(setup.sharedKey);
    });
  }

  protected enableMfa(): Promise<void> {
    return this.act(async () => {
      const result = await this.api.post<{ recoveryCodes: string[] }>('/api/v1/account/mfa/enable', { code: this.code.replace(/\s/g, '') });
      this.codes.set(result.recoveryCodes);
      this.setupKey.set('');
      this.code = '';
      await this.session.signedIn();
    });
  }

  protected newCodes(): Promise<void> {
    return this.act(async () => {
      const result = await this.api.post<{ recoveryCodes: string[] }>('/api/v1/account/mfa/recovery-codes');
      this.codes.set(result.recoveryCodes);
    });
  }

  protected disableMfa(): Promise<void> {
    return this.act(async () => {
      await this.api.post('/api/v1/account/mfa/disable', { password: this.disablePassword });
      this.disablePassword = '';
      this.codes.set([]);
      await this.session.signedIn();
    });
  }

  protected revokeAll(): Promise<void> {
    return this.act(async () => {
      await this.api.post('/api/v1/account/sessions/revoke');
      await this.session.signOut();
      await this.router.navigateByUrl('/sign-in');
    });
  }

  private async act(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set('');
    this.failed.set(false);
    try {
      await action();
    } catch (error) {
      this.failed.set(true);
      this.message.set(errorText(this.i18n, error));
    } finally {
      this.busy.set(false);
    }
  }
}
