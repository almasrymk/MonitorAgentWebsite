import { Component, inject, input } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { I18n, TranslatePipe } from './i18n';
import { saveLanguage, Session } from './session';

export interface NavItem {
  path: string;
  label: string;
}

@Component({
  selector: 'ma-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TranslatePipe],
  template: `
    <a class="skip-link" href="#main">{{ 'skip' | t }}</a>
    <div class="app">
      <aside class="sidebar">
        <a class="logo" routerLink="/">
          <svg aria-hidden="true" width="24" height="24" viewBox="0 0 24 24"><path d="M2 12h4l3-7 4 14 3-7h6" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/></svg>
          {{ 'brand' | t }}
        </a>
        <nav [attr.aria-label]="'nav.label' | t">
          @for (item of nav(); track item.path) {
            <a [routerLink]="item.path" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.path === '/' }" ariaCurrentWhenActive="page">{{ item.label | t }}</a>
          }
        </nav>
        <div class="foot"><a href="/">{{ 'nav.website' | t }}</a></div>
      </aside>
      <div>
        <header class="topbar">
          <button class="link-button" type="button" (click)="switchLanguage()">
            <span class="visually-hidden">{{ 'lang.switchLabel' | t }}: </span><span [attr.lang]="i18n.other()">{{ 'lang.switch' | t }}</span>
          </button>
          <div class="who">
            <strong>{{ session.me()?.fullName }}</strong>
            <div class="muted">{{ session.me()?.email }}</div>
          </div>
          <button class="btn secondary" type="button" (click)="signOut()">{{ 'nav.signOut' | t }}</button>
        </header>
        <main id="main" class="content" tabindex="-1">
          <router-outlet />
        </main>
      </div>
    </div>
  `,
})
/** The signed-in frame of the portal and admin app. The menu comes from the route's data (`nav`). */
export class AppShell {
  readonly nav = input<NavItem[]>([]);
  protected readonly i18n = inject(I18n);
  protected readonly session = inject(Session);
  private readonly router = inject(Router);


  protected switchLanguage(): void {
    saveLanguage(this.i18n, this.i18n.other());
  }

  protected async signOut(): Promise<void> {
    await this.session.signOut();
    await this.router.navigateByUrl('/sign-in');
  }
}
