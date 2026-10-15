import { Component, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Session, TranslatePipe } from 'shared';

/** A customer account opened the admin address. */
@Component({
  selector: 'admin-no-access',
  imports: [TranslatePipe],
  template: `
    <main class="auth-page" id="main">
      <div class="card auth-card">
        <h1>{{ 'noAccess.title' | t }}</h1>
        <p>{{ 'noAccess.body' | t }}</p>
        <p class="row">
          <a class="btn" href="/portal/">{{ 'noAccess.portal' | t }}</a>
          <button class="btn secondary" type="button" (click)="signOut()">{{ 'nav.signOut' | t }}</button>
        </p>
      </div>
    </main>
  `,
})
export class NoAccessPage {
  private readonly session = inject(Session);
  private readonly router = inject(Router);

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/sign-in');
  }
}
